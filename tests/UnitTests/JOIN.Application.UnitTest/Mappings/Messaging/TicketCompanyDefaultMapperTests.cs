using FluentAssertions;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;

namespace JOIN.Application.UnitTest.Mappings.Messaging;

/// <summary>
/// Contains unit tests for <see cref="TicketCompanyDefaultMapper"/> using the real source-generated implementation.
/// </summary>
public sealed class TicketCompanyDefaultMapperTests
{
    private readonly TicketCompanyDefaultMapper _mapper = new();

    /// <summary>
    /// Verifies that all supported properties are mapped from the create command to a new entity.
    /// </summary>
    [Fact]
    public void ToEntity_WhenCreateCommandIsFullyPopulated_ShouldMapAllSupportedFields()
    {
        // Arrange
        var command = CreateValidCreateCommand();

        // Act
        var entity = _mapper.ToEntity(command);

        // Assert
        entity.StartCode.Should().Be(command.StartCode);
        entity.CodeSequenceLength.Should().Be(command.CodeSequenceLength);
        entity.UsePersonalizedCode.Should().Be(command.UsePersonalizedCode);
        entity.TicketStatusDefaultId.Should().Be(command.TicketStatusDefaultId);
        entity.TicketComplexityDefaultId.Should().Be(command.TicketComplexityDefaultId);
        entity.TimeUnitDefaultId.Should().Be(command.TimeUnitDefaultId);
        entity.AreaDefaultId.Should().Be(command.AreaDefaultId);
        entity.ProjectDefaultId.Should().Be(command.ProjectDefaultId);
        entity.ChannelDefaultId.Should().Be(command.ChannelDefaultId);

        entity.Id.Should().NotBeEmpty();
        entity.CompanyId.Should().Be(Guid.Empty);
        entity.Created.Should().NotBe(default);
        entity.CreatedBy.Should().BeNull();
        entity.LastModified.Should().BeNull();
        entity.LastModifiedBy.Should().BeNull();
        entity.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
        entity.MaxDayTicketInactivity.Should().BeNull();
        entity.TicketStatusDefault.Should().BeNull();
        entity.TicketComplexityDefault.Should().BeNull();
        entity.TimeUnitDefault.Should().BeNull();
        entity.AreaDefault.Should().BeNull();
        entity.ProjectDefault.Should().BeNull();
        entity.ChannelDefault.Should().BeNull();
    }

    /// <summary>
    /// Verifies that nullable default identifiers and empty string values are preserved as provided.
    /// </summary>
    [Fact]
    public void ToEntity_WhenOptionalDefaultsAreNull_ShouldMapNullOptionalsAndPreserveEmptyStartCode()
    {
        // Arrange
        var command = CreateValidCreateCommand() with
        {
            StartCode = string.Empty,
            TicketStatusDefaultId = null,
            TicketComplexityDefaultId = null,
            TimeUnitDefaultId = null,
            AreaDefaultId = null,
            ProjectDefaultId = null,
            ChannelDefaultId = null
        };

        // Act
        var entity = _mapper.ToEntity(command);

        // Assert
        entity.StartCode.Should().BeEmpty();
        entity.TicketStatusDefaultId.Should().BeNull();
        entity.TicketComplexityDefaultId.Should().BeNull();
        entity.TimeUnitDefaultId.Should().BeNull();
        entity.AreaDefaultId.Should().BeNull();
        entity.ProjectDefaultId.Should().BeNull();
        entity.ChannelDefaultId.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a null create command throws the current generated null-reference exception.
    /// </summary>
    [Fact]
    public void ToEntity_WhenCreateCommandIsNull_ShouldThrowNullReferenceException()
    {
        // Arrange
        CreateTicketCompanyDefaultCommand command = null!;

        // Act
        Action act = () => _mapper.ToEntity(command);

        // Assert
        act.Should().Throw<NullReferenceException>()
            .WithMessage("Object reference not set to an instance of an object.");
    }

    /// <summary>
    /// Verifies that all supported properties are updated while ignored infrastructure members remain unchanged.
    /// </summary>
    [Fact]
    public void ApplyUpdate_WhenCommandIsFullyPopulated_ShouldUpdateMappedFieldsOnly()
    {
        // Arrange
        var command = CreateValidUpdateCommand();
        var entity = CreateValidEntity();
        var originalId = entity.Id;
        var originalCompanyId = entity.CompanyId;
        var originalCreated = entity.Created;
        var originalCreatedBy = entity.CreatedBy;
        var originalMaxDayTicketInactivity = entity.MaxDayTicketInactivity;

        // Act
        _mapper.ApplyUpdate(command, entity);

        // Assert
        entity.Id.Should().Be(originalId);
        entity.CompanyId.Should().Be(originalCompanyId);
        entity.Created.Should().Be(originalCreated);
        entity.CreatedBy.Should().Be(originalCreatedBy);
        entity.StartCode.Should().Be(command.StartCode);
        entity.CodeSequenceLength.Should().Be(command.CodeSequenceLength);
        entity.UsePersonalizedCode.Should().Be(command.UsePersonalizedCode);
        entity.TicketStatusDefaultId.Should().Be(command.TicketStatusDefaultId);
        entity.TicketComplexityDefaultId.Should().Be(command.TicketComplexityDefaultId);
        entity.TimeUnitDefaultId.Should().Be(command.TimeUnitDefaultId);
        entity.AreaDefaultId.Should().Be(command.AreaDefaultId);
        entity.ProjectDefaultId.Should().Be(command.ProjectDefaultId);
        entity.ChannelDefaultId.Should().Be(command.ChannelDefaultId);
        entity.MaxDayTicketInactivity.Should().Be(originalMaxDayTicketInactivity);
        entity.TicketStatusDefault.Should().BeNull();
        entity.TicketComplexityDefault.Should().BeNull();
        entity.TimeUnitDefault.Should().BeNull();
        entity.AreaDefault.Should().BeNull();
        entity.ProjectDefault.Should().BeNull();
        entity.ChannelDefault.Should().BeNull();
    }

    /// <summary>
    /// Verifies that nullable default identifiers can be cleared during update mapping.
    /// </summary>
    [Fact]
    public void ApplyUpdate_WhenOptionalDefaultsAreNull_ShouldClearOptionalIdentifiers()
    {
        // Arrange
        var command = CreateValidUpdateCommand() with
        {
            TicketStatusDefaultId = null,
            TicketComplexityDefaultId = null,
            TimeUnitDefaultId = null,
            AreaDefaultId = null,
            ProjectDefaultId = null,
            ChannelDefaultId = null
        };
        var entity = CreateValidEntity();

        // Act
        _mapper.ApplyUpdate(command, entity);

        // Assert
        entity.TicketStatusDefaultId.Should().BeNull();
        entity.TicketComplexityDefaultId.Should().BeNull();
        entity.TimeUnitDefaultId.Should().BeNull();
        entity.AreaDefaultId.Should().BeNull();
        entity.ProjectDefaultId.Should().BeNull();
        entity.ChannelDefaultId.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a null update source throws the current generated null-reference exception.
    /// </summary>
    [Fact]
    public void ApplyUpdate_WhenCommandIsNull_ShouldThrowNullReferenceException()
    {
        // Arrange
        UpdateTicketCompanyDefaultCommand command = null!;
        var entity = CreateValidEntity();

        // Act
        Action act = () => _mapper.ApplyUpdate(command, entity);

        // Assert
        act.Should().Throw<NullReferenceException>()
            .WithMessage("Object reference not set to an instance of an object.");
    }

    private static CreateTicketCompanyDefaultCommand CreateValidCreateCommand() => new()
    {
        StartCode = "CRM",
        CodeSequenceLength = 5,
        UsePersonalizedCode = true,
        TicketStatusDefaultId = Guid.NewGuid(),
        TicketComplexityDefaultId = Guid.NewGuid(),
        TimeUnitDefaultId = Guid.NewGuid(),
        AreaDefaultId = Guid.NewGuid(),
        ProjectDefaultId = Guid.NewGuid(),
        ChannelDefaultId = Guid.NewGuid()
    };

    private static UpdateTicketCompanyDefaultCommand CreateValidUpdateCommand() => new()
    {
        Id = Guid.NewGuid(),
        StartCode = "SUP",
        CodeSequenceLength = 8,
        UsePersonalizedCode = false,
        TicketStatusDefaultId = Guid.NewGuid(),
        TicketComplexityDefaultId = Guid.NewGuid(),
        TimeUnitDefaultId = Guid.NewGuid(),
        AreaDefaultId = Guid.NewGuid(),
        ProjectDefaultId = Guid.NewGuid(),
        ChannelDefaultId = Guid.NewGuid()
    };

    private static TicketCompanyDefault CreateValidEntity()
    {
        var entity = new TicketCompanyDefault
        {
            CompanyId = Guid.NewGuid(),
            StartCode = "OLD",
            CodeSequenceLength = 3,
            UsePersonalizedCode = true,
            TicketStatusDefaultId = Guid.NewGuid(),
            TicketComplexityDefaultId = Guid.NewGuid(),
            TimeUnitDefaultId = Guid.NewGuid(),
            AreaDefaultId = Guid.NewGuid(),
            ProjectDefaultId = Guid.NewGuid(),
            ChannelDefaultId = Guid.NewGuid(),
            MaxDayTicketInactivity = 30,
            CreatedBy = "creator"
        };

        SetEntityId(entity, Guid.NewGuid());
        return entity;
    }

    private static void SetEntityId(BaseEntity entity, Guid id)
        => typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);
}