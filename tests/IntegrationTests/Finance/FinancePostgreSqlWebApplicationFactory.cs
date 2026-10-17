// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface.Finance;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Finance;

/// <summary>
/// SPEC 56 — <see cref="CustomWebApplicationFactory"/> plus a switchable <see cref="IBaseCurrencyAmountSource"/>, so
/// the tests can simulate a company that already stores amounts before SPEC 57/61 register real sources.
/// </summary>
public sealed class FinancePostgreSqlWebApplicationFactory : CustomWebApplicationFactory
{
    public SwitchableAmountSource AmountSource { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<IBaseCurrencyAmountSource>(AmountSource));
    }

    /// <summary>Reports amounts only for the companies listed in <see cref="CompaniesWithAmounts"/>.</summary>
    public sealed class SwitchableAmountSource : IBaseCurrencyAmountSource
    {
        public HashSet<Guid> CompaniesWithAmounts { get; } = [];
        public string Name => "TestAmounts";

        public Task<bool> HasAmountsAsync(Guid companyId, CancellationToken cancellationToken = default)
        {
            lock (CompaniesWithAmounts)
            {
                return Task.FromResult(CompaniesWithAmounts.Contains(companyId));
            }
        }
    }
}
