using JOIN.Application.DTO.Common;

namespace JOIN.Application.DTO.Finance;

/// <summary>
/// A currency of the global ISO catalog (SPEC 56).
/// </summary>
public sealed record CurrencyDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public int Decimals { get; init; }
    public bool IsActive { get; init; }
    /// <summary>Companies that use the currency.</summary>
    public int CompaniesCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// A currency the company uses, with its role (base or parallel) and the latest rate (SPEC 56).
/// </summary>
public sealed record CompanyCurrencyDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid CurrencyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public int Decimals { get; init; }
    public bool IsBase { get; init; }
    public bool IsActive { get; init; }
    /// <summary>Latest rate on or before today; <c>null</c> for the base or when the currency has no rate yet.</summary>
    public decimal? LatestRate { get; init; }
    public DateOnly? LatestRateDate { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// A dated exchange rate: 1 unit of <see cref="CurrencyCode"/> = <see cref="Rate"/> units of the base (SPEC 56).
/// </summary>
public sealed record ExchangeRateDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid CurrencyId { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public string BaseCurrencyCode { get; init; } = string.Empty;
    public DateOnly RateDate { get; init; }
    public decimal Rate { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// The rate that applies to a currency on a date (SPEC 56 rule 4): the base is 1; otherwise the rate of the date
/// or the latest earlier one, with the date it comes from.
/// </summary>
public sealed record ExchangeRateResolutionDto
{
    public Guid CurrencyId { get; init; }
    public decimal Rate { get; init; }
    public DateOnly? RateDate { get; init; }
    public bool IsBase { get; init; }
}
