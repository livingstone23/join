// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Audit;
using JOIN.Domain.Audit;
using JOIN.Infrastructure.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JOIN.Application.UnitTest.Audit;

/// <summary>
/// SPEC 29 F11 — verifies the contract of <see cref="AuditLogger"/>.
/// Four cases from the spec: actor fields populated from <see cref="ICurrentUserService"/>;
/// <c>ChangedBy = "System"</c> when user id missing; an empty-diff Updated produces no row;
/// a repository exception is swallowed with a warning log and never propagates.
/// </summary>
public sealed class AuditLoggerTests
{
    [Fact]
    public async Task LogAsync_WhenCalled_PopulatesActorFieldsFromCurrentUserService()
    {
        var repo = new Mock<IAuditLogRepository>();
        repo.Setup(x => x.InsertManyAsync(It.IsAny<IEnumerable<AuditLog>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AuditLog>, CancellationToken>((rows, _) =>
            {
                var row = rows.Single();
                row.CompanyId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
                row.ChangedBy.Should().Be("22222222-2222-2222-2222-222222222222");
                row.IpAddress.Should().Be("10.0.0.1");
            })
            .ReturnsAsync(1);

        var currentUser = BuildCurrentUser(
            userId: "22222222-2222-2222-2222-222222222222",
            companyId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ip: "10.0.0.1");

        var sut = new AuditLogger(currentUser.Object, repo.Object, NullLogger<AuditLogger>.Instance);

        await sut.LogAsync(
            AuditedEntity.Role,
            Guid.NewGuid(),
            AuditAction.Created,
            entityLabel: "Admin",
            newValues: new Dictionary<string, object?> { ["Name"] = "Admin" });

        repo.Verify(x => x.InsertManyAsync(It.IsAny<IEnumerable<AuditLog>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_WhenUserIdMissing_FallsBackToSystemActor()
    {
        var repo = new Mock<IAuditLogRepository>();
        repo.Setup(x => x.InsertManyAsync(It.IsAny<IEnumerable<AuditLog>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AuditLog>, CancellationToken>((rows, _) =>
            {
                rows.Single().ChangedBy.Should().Be("System");
            })
            .ReturnsAsync(1);

        var currentUser = BuildCurrentUser(userId: null, companyId: Guid.NewGuid(), ip: null);

        var sut = new AuditLogger(currentUser.Object, repo.Object, NullLogger<AuditLogger>.Instance);

        await sut.LogAsync(
            AuditedEntity.User,
            Guid.NewGuid(),
            AuditAction.Created,
            newValues: new Dictionary<string, object?> { ["Email"] = "a@b.com" });

        repo.Verify(x => x.InsertManyAsync(It.IsAny<IEnumerable<AuditLog>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_WhenUpdatedDiffIsEmpty_DoesNotPersistRow()
    {
        var repo = new Mock<IAuditLogRepository>();
        var currentUser = BuildCurrentUser(Guid.NewGuid().ToString(), Guid.NewGuid(), null);

        var sut = new AuditLogger(currentUser.Object, repo.Object, NullLogger<AuditLogger>.Instance);

        var sameValues = new Dictionary<string, object?> { ["Flag"] = true };

        await sut.LogAsync(
            AuditedEntity.RoleSystemOption,
            Guid.NewGuid(),
            AuditAction.Updated,
            oldValues: sameValues,
            newValues: sameValues);

        repo.Verify(x => x.InsertManyAsync(It.IsAny<IEnumerable<AuditLog>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogAsync_WhenRepositoryThrows_SwallowsAndDoesNotPropagate()
    {
        var repo = new Mock<IAuditLogRepository>();
        repo.Setup(x => x.InsertManyAsync(It.IsAny<IEnumerable<AuditLog>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("bitácora offline"));

        var currentUser = BuildCurrentUser(Guid.NewGuid().ToString(), Guid.NewGuid(), null);

        var sut = new AuditLogger(currentUser.Object, repo.Object, NullLogger<AuditLogger>.Instance);

        // Must NOT throw — a bitácora failure must never break the calling business flow.
        var act = () => sut.LogAsync(
            AuditedEntity.Role,
            Guid.NewGuid(),
            AuditAction.Created,
            newValues: new Dictionary<string, object?> { ["Name"] = "x" });

        await act.Should().NotThrowAsync();
    }

    private static Mock<ICurrentUserService> BuildCurrentUser(string? userId, Guid companyId, string? ip)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.SetupGet(x => x.UserId).Returns(userId);
        mock.SetupGet(x => x.CompanyId).Returns(companyId);
        mock.SetupGet(x => x.IpAddress).Returns(ip);
        return mock;
    }
}
