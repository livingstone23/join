using System.Globalization;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;

namespace JOIN.Application.UseCases.Messaging.Tickets;

/// <summary>
/// Assigns the next ticket code for the ticket's company, driven by that company's
/// <see cref="TicketCompanyDefault"/> row. Every company keeps its own numbering.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>UsePersonalizedCode = true</c>: <c>{StartCode}-{sequence}</c>, padded to
/// <c>CodeSequenceLength</c>. The sequence is continuous per company (it never restarts,
/// because the code carries no date).</item>
/// <item>No configuration, or <c>UsePersonalizedCode = false</c>: the standard
/// <c>TICK-{yyyyMM}-{sequence}</c>, whose sequence restarts each month per company.</item>
/// </list>
/// The next sequence is the highest one already used under the same prefix plus one,
/// counting soft-deleted tickets too, so deleting a ticket never causes its code to be
/// reissued (the database keeps a unique index on <c>(CompanyId, Code)</c>).
/// </remarks>
public sealed class TicketCodeGenerator(IUnitOfWork unitOfWork)
{
    private const string StandardPrefix = "TICK";

    public async Task AssignCodeAsync(Ticket ticket, DateTime utcNow, CancellationToken cancellationToken)
    {
        var companyId = ticket.CompanyId;

        var defaults = (await unitOfWork.GetRepository<TicketCompanyDefault>().GetAllAsync())
            .FirstOrDefault(x => x.CompanyId == companyId && x.GcRecord == 0);

        var usePersonalizedCode = defaults is { UsePersonalizedCode: true };

        // SetPersonalizedCode normalizes StartCode the same way (trim + upper), so the prefix
        // searched here is exactly the one the generated code will carry.
        var prefix = usePersonalizedCode
            ? $"{defaults!.StartCode.Trim().ToUpperInvariant()}-"
            : $"{StandardPrefix}-{utcNow:yyyyMM}-";

        var companyCodes = (await unitOfWork.GetRepository<Ticket>()
                .GetAllIncludingDeletedAsync(t => t.CompanyId == companyId && t.Code.StartsWith(prefix)))
            .Select(t => t.Code);

        var nextSequence = GetNextSequence(companyCodes, prefix);

        if (usePersonalizedCode)
        {
            ticket.SetPersonalizedCode(defaults!.StartCode, nextSequence, defaults.CodeSequenceLength);
        }
        else
        {
            ticket.SetStandardCode(utcNow.Year, utcNow.Month, nextSequence);
        }
    }

    /// <summary>
    /// Returns the highest numeric suffix found after <paramref name="prefix"/> plus one.
    /// Codes under the prefix whose suffix is not purely numeric are ignored.
    /// </summary>
    private static int GetNextSequence(IEnumerable<string> codes, string prefix)
    {
        var max = 0;

        foreach (var code in codes)
        {
            if (!code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
                && sequence > max)
            {
                max = sequence;
            }
        }

        return max + 1;
    }
}
