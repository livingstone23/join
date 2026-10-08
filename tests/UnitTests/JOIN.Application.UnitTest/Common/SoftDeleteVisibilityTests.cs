// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using Moq;

namespace JOIN.Application.UnitTest.Common;

/// <summary>
/// Unit tests for <see cref="SoftDeleteVisibility"/> (SPEC 41): deleted rows are visible only to
/// a real SuperAdmin who explicitly asks for them; the flag is ignored for everyone else.
/// </summary>
public sealed class SoftDeleteVisibilityTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, null, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, null, false)]
    public void IncludeDeleted_ShouldOnlyBeTrueForSuperAdminWhoRequestsIt(bool isSuperAdmin, bool? requested, bool expected)
    {
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);

        var includeDeleted = SoftDeleteVisibility.IncludeDeleted(currentUserServiceMock.Object, requested);

        includeDeleted.Should().Be(expected);
    }

    [Fact]
    public void IncludeDeleted_WhenCallerIsOnlySuperAdminCompany_ShouldIgnoreRequest()
    {
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.Setup(x => x.IsInRole("SuperAdminCompany")).Returns(true);
        currentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(false);

        var includeDeleted = SoftDeleteVisibility.IncludeDeleted(currentUserServiceMock.Object, true);

        includeDeleted.Should().BeFalse();
    }
}
