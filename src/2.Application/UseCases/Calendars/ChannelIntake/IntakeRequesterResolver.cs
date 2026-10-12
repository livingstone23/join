using Dapper;
using JOIN.Application.Interface;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// Resolves the person who writes to the agent (SPEC 45, "Normalización de identificadores"): by the phone or e-mail
/// of the channel, or by the identification pair. Only active contacts (<c>WhatsApp</c>, <c>MobilePhone</c>,
/// <c>Landline</c> for phones; <c>PrimaryEmail</c>, <c>AlternativeEmail</c> for e-mail) of active persons of the
/// company count. Used by <c>LookupIntakePerson</c> and by <c>RegisterIntakePerson</c> (duplicate and contact
/// checks). When SPEC 47 (tickets by channel) is resumed it moves to <c>UseCases/Admin/Persons</c>.
/// </summary>
public sealed class IntakeRequesterResolver(ISqlConnectionFactory connectionFactory)
{
    /// <summary> Digits compared when a stored number has no country code. </summary>
    public const int LocalDigits = 8;

    private const string PersonColumns = """
        SELECT DISTINCT p.id AS Id,
               TRIM(CONCAT(p.firstname, ' ', COALESCE(p.lastname, ''))) AS FullName,
               EXISTS(SELECT 1 FROM admin.personpendingconfirmations ppc
                       WHERE ppc.companyid = p.companyid AND ppc.personid = p.id
                         AND ppc.status = 1 AND ppc.gcrecord = 0) AS HasPending
          FROM admin.persons p
        """;

    private const string PhoneSql = PersonColumns + """

          JOIN admin.personcontacts pc ON pc.companyid = p.companyid AND pc.personid = p.id
         WHERE p.companyid = @TenantId AND p.gcrecord = 0 AND p.isactive = TRUE
           AND pc.gcrecord = 0 AND pc.isactive = TRUE
           AND pc.contacttype IN (3, 4, 5)
           AND (regexp_replace(pc.contactvalue, '[^0-9]', '', 'g') = @Digits
                OR RIGHT(regexp_replace(pc.contactvalue, '[^0-9]', '', 'g'), 8) = @LocalDigits);
        """;

    private const string EmailSql = PersonColumns + """

          JOIN admin.personcontacts pc ON pc.companyid = p.companyid AND pc.personid = p.id
         WHERE p.companyid = @TenantId AND p.gcrecord = 0 AND p.isactive = TRUE
           AND pc.gcrecord = 0 AND pc.isactive = TRUE
           AND pc.contacttype IN (1, 2)
           AND LOWER(TRIM(pc.contactvalue)) = LOWER(TRIM(@Email));
        """;

    private const string IdentificationSql = PersonColumns + """

         WHERE p.companyid = @TenantId AND p.gcrecord = 0 AND p.isactive = TRUE
           AND p.identificationtypeid = @IdentificationTypeId
           AND p.identificationnumber = @IdentificationNumber;
        """;

    /// <summary> Digits of a number, without the <c>+</c> and separators. </summary>
    public static string Digits(string? phone) =>
        phone is null ? string.Empty : new string(phone.Where(char.IsDigit).ToArray());

    /// <summary>
    /// Looks up by the identifier of <paramref name="channelCode"/>: phone channels by number, the e-mail channel
    /// by address; any other channel finds nobody.
    /// </summary>
    public Task<IntakePersonMatch> LookupByChannelAsync(Guid companyId, string channelCode, string identifier, CancellationToken cancellationToken) =>
        IntakeChannels.IsPhone(channelCode) ? LookupByPhoneAsync(companyId, identifier, cancellationToken)
        : IntakeChannels.IsEmail(channelCode) ? LookupByEmailAsync(companyId, identifier, cancellationToken)
        : Task.FromResult(IntakePersonMatch.None);

    /// <summary>
    /// Compares only digits: the full E.164 number without <c>+</c>, and its last <see cref="LocalDigits"/> digits
    /// for numbers stored without the country code. More than one person is ambiguous.
    /// </summary>
    public async Task<IntakePersonMatch> LookupByPhoneAsync(Guid companyId, string phone, CancellationToken cancellationToken)
    {
        var digits = Digits(phone);
        if (digits.Length < LocalDigits)
        {
            return IntakePersonMatch.None;
        }

        return await QueryAsync(PhoneSql, new { TenantId = companyId, Digits = digits, LocalDigits = digits[^LocalDigits..] }, cancellationToken);
    }

    /// <summary> Exact e-mail, without case and surrounding spaces. More than one person is ambiguous. </summary>
    public async Task<IntakePersonMatch> LookupByEmailAsync(Guid companyId, string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return IntakePersonMatch.None;
        }

        return await QueryAsync(EmailSql, new { TenantId = companyId, Email = email.Trim() }, cancellationToken);
    }

    /// <summary> Exact identification type and number of an active person of the company. </summary>
    public async Task<IntakePersonMatch> LookupByIdentificationAsync(Guid companyId, Guid identificationTypeId, string identificationNumber, CancellationToken cancellationToken)
    {
        if (identificationTypeId == Guid.Empty || string.IsNullOrWhiteSpace(identificationNumber))
        {
            return IntakePersonMatch.None;
        }

        return await QueryAsync(IdentificationSql,
            new { TenantId = companyId, IdentificationTypeId = identificationTypeId, IdentificationNumber = identificationNumber.Trim() },
            cancellationToken);
    }

    private async Task<IntakePersonMatch> QueryAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<PersonRow>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)))
            .DistinctBy(r => r.Id)
            .ToList();

        return rows.Count switch
        {
            0 => IntakePersonMatch.None,
            1 => new IntakePersonMatch(true, rows[0].Id, rows[0].FullName.Trim(), rows[0].HasPending),
            _ => IntakePersonMatch.AmbiguousMatch
        };
    }

    private sealed class PersonRow
    {
        public Guid Id { get; init; }
        public string FullName { get; init; } = string.Empty;
        public bool HasPending { get; init; }
    }
}

/// <summary>
/// Outcome of a requester lookup: only the id and the name travel out, never contact data (SPEC 45).
/// </summary>
public sealed record IntakePersonMatch(bool Found, Guid? PersonId, string? FullName, bool HasPendingConfirmation, bool Ambiguous = false)
{
    /// <summary> No person matched. </summary>
    public static IntakePersonMatch None { get; } = new(false, null, null, false);

    /// <summary> Two or more persons matched: the agent must ask for the identification. </summary>
    public static IntakePersonMatch AmbiguousMatch { get; } = new(false, null, null, false, Ambiguous: true);

    /// <summary> Some person owns the identifier (found or ambiguous). </summary>
    public bool Exists => Found || Ambiguous;
}
