// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Dapper;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// Dapper on PostgreSQL (<see cref="PostgreSqlWebApplicationFactory"/>, <c>postgres:17</c>):
/// <see cref="DapperContext"/> opens Npgsql connections (it used to always create a SqlConnection),
/// and <c>date</c> columns, which Npgsql returns as <see cref="DateOnly"/>, map into <see cref="DateTime"/>.
/// </summary>
public sealed class DapperPostgreSqlTests : IClassFixture<PostgreSqlWebApplicationFactory>
{
    private readonly PostgreSqlWebApplicationFactory _factory;

    public DapperPostgreSqlTests(PostgreSqlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record DateRow(DateTime StartDate, DateTime? EndDate, DateTime? MissingDate);

    [Fact]
    public async Task DapperContext_WithPostgreSqlProvider_OpensNpgsqlConnectionAndReadsPersons()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dapperContext = scope.ServiceProvider.GetRequiredService<DapperContext>();
        using (var connection = dapperContext.CreateConnection())
        {
            connection.Should().BeOfType<NpgsqlConnection>();
        }

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var personId = await db.Persons.IgnoreQueryFilters().Where(p => p.GcRecord == 0).Select(p => p.Id).FirstAsync();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var person = await unitOfWork.Persons.GetAsync(personId);

        person.Should().NotBeNull();
        person!.Id.Should().Be(personId);
    }

    [Fact]
    public async Task DateColumns_AreReadIntoDateTimeProperties()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<ISqlConnectionFactory>();
        using var connection = factory.CreateConnection();

        var row = await connection.QuerySingleAsync<DateRow>(
            "SELECT DATE '2024-01-31' AS StartDate, DATE '2024-12-31' AS EndDate, CAST(NULL AS date) AS MissingDate");

        row.StartDate.Should().Be(new DateTime(2024, 1, 31));
        row.EndDate.Should().Be(new DateTime(2024, 12, 31));
        row.MissingDate.Should().BeNull();
    }
}
