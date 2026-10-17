namespace JOIN.Application.UseCases.Customers.LeadReports;

/// <summary>
/// Definitions of SPEC 64 rule 5: conversion = won ÷ created of the period (integer percent), weighted pipeline =
/// Σ base value × stage probability, average days to close rounded to one decimal.
/// </summary>
public static class LeadReportMath
{
    public static int Conversion(int won, int created) =>
        created <= 0 ? 0 : (int)Math.Round(won * 100m / created, MidpointRounding.AwayFromZero);

    public static decimal Weighted(decimal valueBase, int probability) =>
        Math.Round(valueBase * probability / 100m, 2, MidpointRounding.AwayFromZero);

    public static decimal? Days(double? averageDays) =>
        averageDays is { } days ? Math.Round((decimal)days, 1, MidpointRounding.AwayFromZero) : null;
}
