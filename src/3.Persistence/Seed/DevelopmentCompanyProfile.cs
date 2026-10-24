// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

namespace JOIN.Persistence.Seed;

/// <summary>
/// Perfil de una empresa de desarrollo (SPEC 100): moneda base, monedas paralelas con su tasa
/// (convención de SPEC 56), factor de conversión de los montos de ejemplo y geografía.
/// </summary>
public sealed record DevelopmentCompanyProfile
{
    public required string TaxId { get; init; }
    public required string CompanyName { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required IReadOnlyList<ParallelCurrencySeed> ParallelCurrencies { get; init; }

    /// <summary>Unidades de la moneda base por 1 unidad de USD (los montos de ejemplo de JOIN-001).</summary>
    public required decimal ExampleAmountFactor { get; init; }
    public required string CountryIsoCode { get; init; }
    public required IReadOnlyList<string> MunicipalityNames { get; init; }
    public required string TimeZoneId { get; init; }

    /// <summary>
    /// Converts a sample amount of JOIN-001 (USD) to the base currency of the profile: exact, 2 decimals
    /// (<see cref="MidpointRounding.AwayFromZero"/>, rule 3 of SPEC 56). 100 USD → 3.662,00 NIO.
    /// </summary>
    public decimal ToBaseAmount(decimal exampleAmount) =>
        ExampleAmountFactor == 1m ? exampleAmount : Math.Round(exampleAmount * ExampleAmountFactor, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Moneda paralela de una empresa de desarrollo: 1 unidad = <paramref name="Rate"/> unidades de la base.</summary>
public sealed record ParallelCurrencySeed(string Code, decimal Rate);

/// <summary>
/// SPEC 100: the development companies. <see cref="Join001"/> is the master company (USD base, its data does not
/// change); <see cref="Nicaragua"/> receives the same seed in córdobas and is created only in Development.
/// </summary>
public static class DevelopmentCompanyProfiles
{
    public static readonly DevelopmentCompanyProfile Join001 = new()
    {
        TaxId = "JOIN-001",
        CompanyName = "JOIN Software Group",
        BaseCurrencyCode = "USD",
        ParallelCurrencies = [new("DOP", 0.016846m), new("EUR", 1.080000m)],
        ExampleAmountFactor = 1m,
        CountryIsoCode = "HN",
        MunicipalityNames = ["San Pedro Sula"],
        TimeZoneId = "America/Managua"
    };

    public static readonly DevelopmentCompanyProfile Nicaragua = new()
    {
        TaxId = "JOIN-NI-001",
        CompanyName = "JOIN Nicaragua",
        BaseCurrencyCode = "NIO",
        ParallelCurrencies = [new("USD", 36.62m)],
        ExampleAmountFactor = 36.62m,
        CountryIsoCode = "NI",
        MunicipalityNames =
        [
            "Managua", "Ciudad Sandino", "Tipitapa", "Ticuantepe",
            "Leon", "Nagarote", "La Paz Centro", "Quezalguaque",
            "Matagalpa", "Matiguas", "Sebaco", "Rio Blanco",
            "Chinandega", "Corinto", "El Viejo", "Chichigalpa",
            "Masaya", "Nindiri", "Catarina", "Niquinohomo"
        ],
        TimeZoneId = "America/Managua"
    };

    /// <summary>Every development company, master first.</summary>
    public static readonly IReadOnlyList<DevelopmentCompanyProfile> All = [Join001, Nicaragua];

    /// <summary>The profile of a development company by its <c>TaxId</c>, or <c>null</c> for any other company.</summary>
    public static DevelopmentCompanyProfile? FindByTaxId(string? taxId) =>
        All.FirstOrDefault(p => string.Equals(p.TaxId, taxId, StringComparison.OrdinalIgnoreCase));
}
