using System.Data;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Domain.Enums;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// Resolves a person from a channel identifier (phone / e-mail) or an identification pair (SPEC 45, "Normalización
/// de identificadores"). Shared by <c>LookupIntakePerson</c>, <c>RegisterIntakePerson</c> and the reschedule/cancel
/// ownership check. Lives here for now; the spec notes that when SPEC 47 (ticket intake) is resumed it should
/// move to <c>UseCases/Admin/Persons</c>.
/// <para>
/// Scoped: the connection factory is held for the lifetime of one request.
/// </para>
/// </summary>
public sealed class IntakeRequesterResolver(ISqlConnectionFactory connectionFactory)
{
    /// <summary> Strip every non-digit and the leading <c>+</c> from the agent's E.164 number. </summary>
    public static string NormalizePhone(string e164)
    {
        if (string.IsNullOrWhiteSpace(e164))
        {
            return string.Empty;
        }

        var trimmed = e164.Trim();
        if (trimmed.StartsWith('+'))
        {
            trimmed = trimmed[1..];
        }

        return new string(trimmed.Where(char.IsDigit).ToArray());
    }

    /// <summary>
    /// Phone search (SPEC 45). The SQL pulls every active person contact of phone type with the digit-only form
    /// of its value; the C# side keeps the rows where it equals the full E.164 digits or its last 8 digits
    /// (people sometimes save numbers without the country code). One distinct person: <see cref="LookupPersonResult.Found"/>;
    /// two or more: <see cref="LookupPersonResult.Ambiguous"/> so the agent asks for identification.
    /// </summary>
    public async Task<LookupPersonResult> LookupByPhoneAsync(Guid companyId, string e164Phone, CancellationToken cancellationToken)
    {
        var normalized = NormalizePhone(e164Phone);
        if (string.IsNullOrEmpty(normalized))
        {
            return LookupPersonResult.NotFound();
        }

        var last8 = normalized.Length >= 8 ? normalized[^8..] : normalized;

        const string sql = """
            SELECT p.id,
                   p.firstname || ' ' || COALESCE(p.lastname, '') AS fullname,
                   regexp_replace(pc.contactvalue, '[^0-9]', '', 'g') AS digits,
                   RIGHT(regexp_replace(pc.contactvalue, '[^0-9]', '', 'g'), 8) AS last8,
                   EXISTS(
                       SELECT 1 FROM admin.personpendingconfirmations ppc
                       WHERE ppc.companyid = p.companyid
                         AND ppc.personid = p.id
                         AND ppc.status = 1
                         AND ppc.gcrecord = 0
                   ) AS haspending
              FROM admin.persons p
              JOIN admin.personcontacts pc
                ON pc.companyid = p.companyid
               AND pc.personid = p.id
             WHERE p.companyid = @CompanyId
               AND p.gcrecord = 0
               AND p.isactive = TRUE
               AND pc.gcrecord = 0
               AND pc.isactive = TRUE
               AND pc.contacttype IN (3, 4, 5)
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<PhoneRow>(
            new CommandDefinition(sql, new { CompanyId = companyId, NormalizedPhone = normalized, Last8Digits = last8 }, cancellationToken: cancellationToken)))
            .AsList();

        var matches = rows
            .Where(r => r.Digits == normalized || (!string.IsNullOrEmpty(r.Last8) && r.Last8 == last8))
            .GroupBy(r => r.Id)
            .Select(g => new { g.Key, FullName = g.First().FullName, HasPending = g.Any(r => r.HasPending) })
            .ToList();

        if (matches.Count == 0)
        {
            return LookupPersonResult.NotFound();
        }

        if (matches.Count > 1)
        {
            return LookupPersonResult.AmbiguousResult();
        }

        var only = matches[0];
        return LookupPersonResult.FoundResult(only.Key, only.FullName, only.HasPending);
    }

    /// <summary>
    /// E-mail search (SPEC 45). Case-insensitive match on the trimmed <c>contactvalue</c>, only e-mail contact
    /// types. Two or more rows: PostgreSQL's collation-sensitive match is exact by design; we keep the first row
    /// and would log a warning if more than one ever comes back.
    /// </summary>
    public async Task<LookupPersonResult> LookupByEmailAsync(Guid companyId, string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return LookupPersonResult.NotFound();
        }

        const string sql = """
            SELECT p.id,
                   p.firstname || ' ' || COALESCE(p.lastname, '') AS fullname,
                   EXISTS(
                       SELECT 1 FROM admin.personpendingconfirmations ppc
                       WHERE ppc.companyid = p.companyid
                         AND ppc.personid = p.id
                         AND ppc.status = 1
                         AND ppc.gcrecord = 0
                   ) AS haspending
              FROM admin.persons p
              JOIN admin.personcontacts pc
                ON pc.companyid = p.companyid
               AND pc.personid = p.id
             WHERE p.companyid = @CompanyId
               AND p.gcrecord = 0
               AND p.isactive = TRUE
               AND pc.gcrecord = 0
               AND pc.isactive = TRUE
               AND pc.contacttype IN (1, 2)
               AND LOWER(TRIM(pc.contactvalue)) = LOWER(TRIM(@Email))
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<EmailRow>(
            new CommandDefinition(sql, new { CompanyId = companyId, Email = email.Trim() }, cancellationToken: cancellationToken)))
            .AsList();

        if (rows.Count == 0)
        {
            return LookupPersonResult.NotFound();
        }

        var row = rows[0];
        return LookupPersonResult.FoundResult(row.Id, row.FullName, row.HasPending);
    }

    private sealed class PhoneRow
    {
        public Guid Id { get; init; }
        public string FullName { get; init; } = string.Empty;
        public string? Digits { get; init; }
        public string? Last8 { get; init; }
        public bool HasPending { get; init; }
    }

    private sealed class EmailRow
    {
        public Guid Id { get; init; }
        public string FullName { get; init; } = string.Empty;
        public bool HasPending { get; init; }
    }
}

/// <summary>
/// Outcome of a person lookup (SPEC 45). Only <see cref="PersonId"/> and <see cref="FullName"/> travel out of the
/// resolver — never contact data.
/// </summary>
public sealed record LookupPersonResult(
    bool Found,
    Guid? PersonId,
    string? FullName,
    bool HasPendingConfirmation,
    bool Ambiguous = false)
{
    public static LookupPersonResult NotFound() => new(false, null, null, false);
    public static LookupPersonResult FoundResult(Guid personId, string fullName, bool hasPending) =>
        new(true, personId, fullName, hasPending);
    public static LookupPersonResult AmbiguousResult() => new(false, null, null, false, Ambiguous: true);
}