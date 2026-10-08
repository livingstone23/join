// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.Common;

/// <summary>
/// Unit tests for <see cref="ActiveDependentsCheck"/> (SPEC 41, section E).
/// </summary>
public sealed class ActiveDependentsCheckTests
{
    private static readonly Guid ParentId = Guid.NewGuid();

    [Fact]
    public async Task CountAsync_WhenNoChildMatches_ShouldHaveNoDependents()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        unitOfWorkMock.SetupRepositoryRows<Province>([NewProvince(Guid.NewGuid())]);

        var check = await new ActiveDependentsCheck(unitOfWorkMock.Object)
            .CountAsync<Province>(p => p.GcRecord == 0 && p.CountryId == ParentId, "provinces");

        check.HasDependents.Should().BeFalse();
        check.Details.Should().BeEmpty();
    }

    [Fact]
    public async Task CountAsync_ShouldCountOnlyRowsMatchingThePredicate()
    {
        var deleted = NewProvince(ParentId);
        deleted.MarkAsDeleted();
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        unitOfWorkMock.SetupRepositoryRows<Province>([NewProvince(ParentId), NewProvince(ParentId), deleted, NewProvince(Guid.NewGuid())]);

        var check = await new ActiveDependentsCheck(unitOfWorkMock.Object)
            .CountAsync<Province>(p => p.GcRecord == 0 && p.CountryId == ParentId, "provinces");

        check.HasDependents.Should().BeTrue();
        check.Details.Should().Equal("Active provinces: 2");
    }

    [Fact]
    public async Task CountAsync_ShouldListEachChildTypeWithRowsInCallOrder()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        unitOfWorkMock.SetupRepositoryRows<Province>([NewProvince(ParentId)]);
        unitOfWorkMock.SetupRepositoryRows<Region>([]);
        unitOfWorkMock.SetupRepositoryRows<PersonAddress>(
        [
            new PersonAddress { CompanyId = Guid.NewGuid(), CountryId = ParentId },
            new PersonAddress { CompanyId = Guid.NewGuid(), CountryId = ParentId }
        ]);

        var check = new ActiveDependentsCheck(unitOfWorkMock.Object);
        await check.CountAsync<Province>(p => p.GcRecord == 0 && p.CountryId == ParentId, "provinces");
        await check.CountAsync<Region>(r => r.GcRecord == 0 && r.CountryId == ParentId, "regions");
        await check.CountAsync<PersonAddress>(a => a.GcRecord == 0 && a.CountryId == ParentId, "person addresses");

        check.Details.Should().Equal("Active provinces: 1", "Active person addresses: 2");
    }

    private static Province NewProvince(Guid countryId) => new() { Name = "Managua", Code = "MN", CountryId = countryId };
}
