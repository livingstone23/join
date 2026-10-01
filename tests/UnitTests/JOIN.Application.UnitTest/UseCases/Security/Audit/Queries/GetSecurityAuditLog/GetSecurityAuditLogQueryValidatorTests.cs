using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.UseCases.Security.Audit.Queries.GetSecurityAuditLog;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UnitTest.UseCases.Security.Audit.Queries.GetSecurityAuditLog;

/// <summary>
/// Contains the unit tests for the security audit log query validator.
/// </summary>
public sealed class GetSecurityAuditLogQueryValidatorTests
{
    private readonly GetSecurityAuditLogQueryValidator _validator =
        new(Options.Create(new PaginationSettings { MinPageSize = 1, MaxPageSize = 50 }));

    [Fact]
    public void Validate_WhenQueryIsValid_ShouldPass()
        => _validator.Validate(new GetSecurityAuditLogQuery(FromDate: new DateTime(2026, 1, 1), ToDate: new DateTime(2026, 2, 1)))
            .IsValid.Should().BeTrue();

    [Fact]
    public void Validate_WhenPagingIsOutOfRange_ShouldFail()
        => _validator.Validate(new GetSecurityAuditLogQuery(PageNumber: 0, PageSize: 500))
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["PageNumber", "PageSize"]);

    [Fact]
    public void Validate_WhenFromDateIsAfterToDate_ShouldFail()
        => _validator.Validate(new GetSecurityAuditLogQuery(FromDate: new DateTime(2026, 3, 1), ToDate: new DateTime(2026, 2, 1)))
            .Errors.Should().ContainSingle(e => e.ErrorMessage == "FromDate must be on or before ToDate.");
}
