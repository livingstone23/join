using FluentAssertions;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;

namespace JOIN.Application.UnitTest.Domain.Audit;

/// <summary>
/// Pure unit tests for the SPEC 41 domain pieces: <see cref="BaseAuditableEntity.IsDeleted"/>,
/// <see cref="BaseAuditableEntity.Restore"/>, <see cref="ApplicationUser.Restore"/> and
/// <see cref="ApplicationRole.Restore"/>.
/// </summary>
public sealed class SoftDeleteRestoreTests
{
    private static readonly DateTime DeletedOnUtc = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsDeleted_NewEntity_ShouldBeFalse()
    {
        var gender = Gender.Create(Guid.NewGuid(), "M", "Male");

        gender.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void IsDeleted_AfterMarkAsDeleted_ShouldBeTrue()
    {
        var gender = Gender.Create(Guid.NewGuid(), "M", "Male");

        gender.MarkAsDeleted(DeletedOnUtc);

        gender.IsDeleted.Should().BeTrue();
        gender.GcRecord.Should().Be(20260928);
    }

    [Fact]
    public void Restore_DeletedEntity_ShouldResetGcRecordToActive()
    {
        var gender = Gender.Create(Guid.NewGuid(), "M", "Male");
        gender.MarkAsDeleted(DeletedOnUtc);

        gender.Restore();

        gender.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
        gender.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Restore_ActiveEntity_ShouldBeIdempotent()
    {
        var gender = Gender.Create(Guid.NewGuid(), "M", "Male");

        gender.Restore();
        gender.Restore();

        gender.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
        gender.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void ApplicationUser_Restore_ShouldResetGcRecordToActive()
    {
        var user = new ApplicationUser { GcRecord = 20260928 };

        user.Restore();

        user.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
    }

    [Fact]
    public void ApplicationUser_Restore_ActiveUser_ShouldBeIdempotent()
    {
        var user = new ApplicationUser();

        user.Restore();

        user.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
    }

    [Fact]
    public void ApplicationRole_Restore_ShouldResetGcRecordToActive()
    {
        var role = new ApplicationRole { GcRecord = 20260928 };

        role.Restore();

        role.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
    }

    [Fact]
    public void ApplicationRole_Restore_ActiveRole_ShouldBeIdempotent()
    {
        var role = new ApplicationRole();

        role.Restore();

        role.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
    }
}
