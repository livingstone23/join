using JOIN.Application.Common;
using JOIN.Application.Interface.Finance;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.Leads.Common;

/// <summary>
/// Estimated value of a lead (SPEC 61 rules 8–9, B.11): with live product lines, the sum of the lines that are not
/// rejected; without lines, the manual value. Each time it runs, the rate of the day is resolved for the lead currency
/// and <c>ExchangeRate</c>, <c>ExchangeRateDate</c> and <c>EstimatedValueBase</c> are stored.
/// </summary>
public sealed class LeadValueCalculator(IUnitOfWork unitOfWork, IExchangeRateResolver rates, IMoneyConverter converter)
{
    /// <summary>Resolves today's rate of <paramref name="currencyId"/>; <c>null</c> error when found.</summary>
    public async Task<(ExchangeRateResolution? Rate, Response<T>? Error)> RateAsync<T>(Guid companyId, Guid currencyId, CancellationToken cancellationToken)
    {
        var rate = await rates.ResolveAsync(companyId, currencyId, Today, cancellationToken);
        return rate is null
            ? (null, Response<T>.Error(LeadErrorCodes.ExchangeRateNotFound, ["The currency is not active in the company or has no exchange rate for today."]))
            : (rate, null);
    }

    /// <summary>
    /// Recalculates <paramref name="lead"/>. <paramref name="manualValue"/> is used only when the lead has no live lines;
    /// <paramref name="pendingLines"/> are lines added or changed in this command and not saved yet.
    /// </summary>
    public async Task<Response<T>?> RecalculateAsync<T>(Lead lead, decimal? manualValue, IEnumerable<LeadProduct>? pendingLines, CancellationToken cancellationToken)
    {
        var (rate, error) = await RateAsync<T>(lead.CompanyId, lead.CurrencyId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var lines = await LiveLinesAsync(lead.Id, pendingLines);
        if (lines.Count > 0)
        {
            lead.EstimatedValue = lines.Where(l => l.Status != LeadProductStatus.Rejected).Sum(l => l.LineTotal);
        }
        else if (manualValue is { } value)
        {
            lead.EstimatedValue = value;
        }

        lead.ExchangeRate = rate!.Rate;
        lead.ExchangeRateDate = rate.RateDate ?? Today;
        lead.EstimatedValueBase = converter.ToBase(lead.EstimatedValue, rate.Rate);
        return null;
    }

    /// <summary>Whether the lead has live product lines (the currency is then locked, rule 8).</summary>
    public async Task<bool> HasLinesAsync(Guid leadId) =>
        (await unitOfWork.GetRepository<LeadProduct>().GetAllIncludingDeletedAsync(l => l.LeadId == leadId && l.GcRecord == 0)).Any();

    private async Task<List<LeadProduct>> LiveLinesAsync(Guid leadId, IEnumerable<LeadProduct>? pending)
    {
        var pendingList = pending?.ToList() ?? [];
        var pendingIds = pendingList.Select(p => p.Id).ToHashSet();
        var stored = (await unitOfWork.GetRepository<LeadProduct>().GetAllIncludingDeletedAsync(l => l.LeadId == leadId && l.GcRecord == 0))
            .Where(l => !pendingIds.Contains(l.Id));
        return stored.Concat(pendingList.Where(p => p.GcRecord == 0)).ToList();
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
