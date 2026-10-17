using System.Globalization;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.Leads.Common;

/// <summary>
/// Lead codes (SPEC 61 B.8): the company's <c>CodePrefix</c> plus a per-company sequence of <c>CodeSequenceLength</c>
/// digits (<c>LD-000001</c>). The sequence is taken under <see cref="IUnitOfWork.AcquireLeadCodeLockAsync"/> so two
/// concurrent creates never share a number; codes with another prefix (before a prefix change) do not count.
/// </summary>
public sealed class LeadCodeGenerator(IUnitOfWork unitOfWork, ILeadCompanySettingsProvider settingsProvider)
{
    public async Task<string> GenerateNextAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        await unitOfWork.AcquireLeadCodeLockAsync(companyId, cancellationToken);
        var settings = await settingsProvider.GetAsync(companyId);
        var prefix = settings.CodePrefix;

        var codes = (await unitOfWork.GetRepository<Lead>().GetAllIncludingDeletedAsync(l => l.CompanyId == companyId && l.Code.StartsWith(prefix)))
            .Select(l => l.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var max = codes.Select(code => TryParse(code, prefix)).Where(n => n.HasValue).Select(n => n!.Value).DefaultIfEmpty(0).Max();

        return Format(prefix, max + 1, settings.CodeSequenceLength);
    }

    public static string Format(string prefix, long sequence, int length) =>
        $"{prefix}{sequence.ToString($"D{length}", CultureInfo.InvariantCulture)}";

    public static long? TryParse(string code, string prefix) =>
        code.Length > prefix.Length
        && code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && long.TryParse(code[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}
