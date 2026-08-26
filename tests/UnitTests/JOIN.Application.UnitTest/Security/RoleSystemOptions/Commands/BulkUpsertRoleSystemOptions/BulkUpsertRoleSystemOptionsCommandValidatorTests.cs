using FluentAssertions;
using FluentValidation.TestHelper;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Commands.BulkUpsertRoleSystemOptions;

namespace JOIN.Application.UnitTest.Security.RoleSystemOptions.Commands.BulkUpsertRoleSystemOptions;

/// <summary>
/// Tests for the FluentValidation rules on <see cref="BulkUpsertRoleSystemOptionsCommand"/>
/// (SPEC 25). Verifies role id presence, items not null/empty, the 500-item cap, and the
/// duplicate <c>SystemOptionId</c> rejection.
/// </summary>
public sealed class BulkUpsertRoleSystemOptionsCommandValidatorTests
{
    private static UpsertRoleSystemOptionItemDto Item(Guid? optionId = null) => new(
        SystemOptionId: optionId ?? Guid.NewGuid(),
        CanRead: true, CanCreate: false, CanUpdate: false, CanDelete: false,
        CanDownload: false, CanExport: false, CanExecute: false);

    private static BulkUpsertRoleSystemOptionsCommand BuildValidCommand() => new(
        RoleId: Guid.NewGuid(),
        Items: new List<UpsertRoleSystemOptionItemDto>
        {
            Item(),
            Item()
        });

    private readonly BulkUpsertRoleSystemOptionsCommandValidator _validator = new();

    /// <summary>
    /// RoleId = Guid.Empty fails validation.
    /// </summary>
    [Fact]
    public void Validate_WhenRoleIdIsEmpty_ShouldHaveError()
    {
        var cmd = BuildValidCommand() with { RoleId = Guid.Empty };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.RoleId);
    }

    /// <summary>
    /// Items = null fails with a null check.
    /// </summary>
    [Fact]
    public void Validate_WhenItemsIsNull_ShouldHaveError()
    {
        var cmd = new BulkUpsertRoleSystemOptionsCommand(Guid.NewGuid(), null!);
        var result = _validator.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Items"));
    }

    /// <summary>
    /// Items = empty list fails with BULK_EMPTY error code.
    /// </summary>
    [Fact]
    public void Validate_WhenItemsIsEmpty_ShouldHaveBulkEmptyError()
    {
        var cmd = BuildValidCommand() with { Items = new List<UpsertRoleSystemOptionItemDto>() };
        var result = _validator.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "BULK_EMPTY");
    }

    /// <summary>
    /// Items.Count > 500 fails with BULK_TOO_LARGE error code.
    /// </summary>
    [Fact]
    public void Validate_WhenItemsExceedsMax_ShouldHaveBulkTooLargeError()
    {
        var items = Enumerable.Range(0, 501)
            .Select(_ => Item())
            .ToList();
        var cmd = BuildValidCommand() with { Items = items };
        var result = _validator.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.StartsWith("BULK_TOO_LARGE"));
    }

    /// <summary>
    /// Items with duplicate SystemOptionId fails with BULK_DUPLICATE_OPTION error code.
    /// </summary>
    [Fact]
    public void Validate_WhenItemsHaveDuplicateSystemOptionId_ShouldHaveBulkDuplicateOptionError()
    {
        var dup = Guid.NewGuid();
        var cmd = BuildValidCommand() with
        {
            Items = new List<UpsertRoleSystemOptionItemDto>
            {
                Item(dup),
                Item(dup)
            }
        };
        var result = _validator.TestValidate(cmd);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "BULK_DUPLICATE_OPTION");
    }

    /// <summary>
    /// Items with 500 unique SystemOptionIds passes the cap check.
    /// </summary>
    [Fact]
    public void Validate_WhenItemsCountIs500Unique_ShouldPassCapRule()
    {
        var items = Enumerable.Range(0, 500)
            .Select(_ => Item())
            .ToList();
        var cmd = BuildValidCommand() with { Items = items };
        var result = _validator.TestValidate(cmd);
        // May still fail on duplicate (impossible here — all unique) or per-item (impossible).
        // The cap rule must not trigger.
        result.Errors.Should().NotContain(e => e.ErrorMessage.StartsWith("BULK_TOO_LARGE"));
    }

    /// <summary>
    /// Items with a SystemOptionId = Guid.Empty fails the per-item rule.
    /// </summary>
    [Fact]
    public void Validate_WhenItemHasEmptySystemOptionId_ShouldHavePerItemError()
    {
        var cmd = BuildValidCommand() with
        {
            Items = new List<UpsertRoleSystemOptionItemDto>
            {
                Item(),
                Item(Guid.Empty)
            }
        };
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor("Items[1].SystemOptionId");
    }
}
