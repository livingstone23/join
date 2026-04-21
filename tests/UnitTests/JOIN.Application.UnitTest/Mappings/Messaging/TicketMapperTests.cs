using FluentAssertions;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Messaging.Tickets.Commands;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;

namespace JOIN.Application.UnitTest.Mappings.Messaging;

/// <summary>
/// Contains unit tests for <see cref="TicketMapper"/> using the real source-generated implementation.
/// </summary>
public sealed class TicketMapperTests
{
    private readonly TicketMapper _mapper = new();

    /// <summary>
    /// Verifies that all supported properties are mapped from the create command to a new ticket entity.
    /// </summary>
    [Fact]
    public void ToEntity_WhenCreateCommandIsFullyPopulated_ShouldMapAllSupportedFields()
    {
        // Arrange
        var command = CreateValidCreateCommand();

        // Act
        var entity = _mapper.ToEntity(command);

        // Assert
        entity.Name.Should().Be(command.Name);
        entity.Description.Should().Be(command.Description);
        entity.EstimatedTime.Should().Be(command.EstimatedTime);
        entity.ConsumedTime.Should().Be(command.ConsumedTime);
        entity.EffortPoints.Should().Be(command.EffortPoints);
        entity.IsVisibleToExternals.Should().Be(command.IsVisibleToExternals);
        entity.TicketStatusId.Should().Be(command.TicketStatusId);
        entity.TicketComplexityId.Should().Be(command.TicketComplexityId);
        entity.TimeUnitId.Should().Be(command.TimeUnitId);
        entity.CustomerId.Should().Be(command.CustomerId);
        entity.ProjectId.Should().Be(command.ProjectId);
        entity.AreaId.Should().Be(command.AreaId);
        entity.ChannelId.Should().Be(command.ChannelId);
        entity.AssignedToUserId.Should().Be(command.AssignedToUserId);
        entity.PrecedentTicketId.Should().Be(command.PrecedentTicketId);

        entity.Id.Should().NotBeEmpty();
        entity.CompanyId.Should().Be(Guid.Empty);
        entity.Code.Should().BeEmpty();
        entity.CreatedByUserId.Should().Be(Guid.Empty);
        entity.Created.Should().NotBe(default);
        entity.CreatedBy.Should().BeNull();
        entity.LastModified.Should().BeNull();
        entity.LastModifiedBy.Should().BeNull();
        entity.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
        entity.Customer.Should().BeNull();
        entity.Project.Should().BeNull();
        entity.Area.Should().BeNull();
        entity.Channel.Should().BeNull();
        entity.CreatedByUser.Should().BeNull();
        entity.AssignedToUser.Should().BeNull();
        entity.Status.Should().BeNull();
        entity.Complexity.Should().BeNull();
        entity.TimeUnit.Should().BeNull();
        entity.PrecedentTicket.Should().BeNull();
        entity.Notifications.Should().BeEmpty();
        entity.ChildTickets.Should().BeEmpty();
        entity.TicketLogs.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that nullable relationships can be cleared during create mapping.
    /// </summary>
    [Fact]
    public void ToEntity_WhenOptionalIdentifiersAreNull_ShouldMapNullOptionalRelationships()
    {
        // Arrange
        var command = CreateValidCreateCommand() with
        {
            CustomerId = null,
            ProjectId = null,
            AreaId = null,
            AssignedToUserId = null,
            PrecedentTicketId = null,
            EffortPoints = null
        };

        // Act
        var entity = _mapper.ToEntity(command);

        // Assert
        entity.CustomerId.Should().BeNull();
        entity.ProjectId.Should().BeNull();
        entity.AreaId.Should().BeNull();
        entity.AssignedToUserId.Should().BeNull();
        entity.PrecedentTicketId.Should().BeNull();
        entity.EffortPoints.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a null create command throws the current generated null-reference exception.
    /// </summary>
    [Fact]
    public void ToEntity_WhenCreateCommandIsNull_ShouldThrowNullReferenceException()
    {
        // Arrange
        CreateTicketCommand command = null!;

        // Act
        Action act = () => _mapper.ToEntity(command);

        // Assert
        act.Should().Throw<NullReferenceException>()
            .WithMessage("Object reference not set to an instance of an object.");
    }

    /// <summary>
    /// Verifies that all supported properties are updated while ignored fields remain untouched.
    /// </summary>
    [Fact]
    public void ApplyUpdate_WhenCommandIsFullyPopulated_ShouldUpdateMappedFieldsOnly()
    {
        // Arrange
        var command = CreateValidUpdateCommand();
        var ticket = CreateValidTicket();
        var originalId = ticket.Id;
        var originalCompanyId = ticket.CompanyId;
        var originalCode = ticket.Code;
        var originalCreatedByUserId = ticket.CreatedByUserId;
        var originalCreated = ticket.Created;
        var originalNotifications = ticket.Notifications;
        var originalChildTickets = ticket.ChildTickets;
        var originalLogs = ticket.TicketLogs;

        // Act
        _mapper.ApplyUpdate(command, ticket);

        // Assert
        ticket.Id.Should().Be(originalId);
        ticket.CompanyId.Should().Be(originalCompanyId);
        ticket.Code.Should().Be(originalCode);
        ticket.CreatedByUserId.Should().Be(originalCreatedByUserId);
        ticket.Created.Should().Be(originalCreated);
        ticket.Name.Should().Be(command.Name);
        ticket.Description.Should().Be(command.Description);
        ticket.EstimatedTime.Should().Be(command.EstimatedTime);
        ticket.ConsumedTime.Should().Be(command.ConsumedTime);
        ticket.EffortPoints.Should().Be(command.EffortPoints);
        ticket.IsVisibleToExternals.Should().Be(command.IsVisibleToExternals);
        ticket.TicketStatusId.Should().Be(command.TicketStatusId);
        ticket.TicketComplexityId.Should().Be(command.TicketComplexityId);
        ticket.TimeUnitId.Should().Be(command.TimeUnitId);
        ticket.CustomerId.Should().Be(command.CustomerId);
        ticket.ProjectId.Should().Be(command.ProjectId);
        ticket.AreaId.Should().Be(command.AreaId);
        ticket.ChannelId.Should().Be(command.ChannelId);
        ticket.AssignedToUserId.Should().Be(command.AssignedToUserId);
        ticket.PrecedentTicketId.Should().Be(command.PrecedentTicketId);
        ticket.Notifications.Should().BeSameAs(originalNotifications);
        ticket.ChildTickets.Should().BeSameAs(originalChildTickets);
        ticket.TicketLogs.Should().BeSameAs(originalLogs);
    }

    /// <summary>
    /// Verifies that nullable relationships can be cleared during update mapping.
    /// </summary>
    [Fact]
    public void ApplyUpdate_WhenOptionalIdentifiersAreNull_ShouldClearOptionalRelationships()
    {
        // Arrange
        var command = CreateValidUpdateCommand() with
        {
            CustomerId = null,
            ProjectId = null,
            AreaId = null,
            AssignedToUserId = null,
            PrecedentTicketId = null,
            EffortPoints = null
        };
        var ticket = CreateValidTicket();

        // Act
        _mapper.ApplyUpdate(command, ticket);

        // Assert
        ticket.CustomerId.Should().BeNull();
        ticket.ProjectId.Should().BeNull();
        ticket.AreaId.Should().BeNull();
        ticket.AssignedToUserId.Should().BeNull();
        ticket.PrecedentTicketId.Should().BeNull();
        ticket.EffortPoints.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a null update source throws the current generated null-reference exception.
    /// </summary>
    [Fact]
    public void ApplyUpdate_WhenCommandIsNull_ShouldThrowNullReferenceException()
    {
        // Arrange
        UpdateTicketCommand command = null!;
        var ticket = CreateValidTicket();

        // Act
        Action act = () => _mapper.ApplyUpdate(command, ticket);

        // Assert
        act.Should().Throw<NullReferenceException>()
            .WithMessage("Object reference not set to an instance of an object.");
    }

    private static CreateTicketCommand CreateValidCreateCommand() => new()
    {
        Name = "CRM incident",
        Description = "Ticket description",
        EstimatedTime = 12.5m,
        ConsumedTime = 3.25m,
        EffortPoints = 8.5m,
        IsVisibleToExternals = true,
        TicketStatusId = Guid.NewGuid(),
        TicketComplexityId = Guid.NewGuid(),
        TimeUnitId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        AreaId = Guid.NewGuid(),
        ChannelId = Guid.NewGuid(),
        AssignedToUserId = Guid.NewGuid(),
        PrecedentTicketId = Guid.NewGuid()
    };

    private static UpdateTicketCommand CreateValidUpdateCommand() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Updated incident",
        Description = "Updated description",
        EstimatedTime = 6.75m,
        ConsumedTime = 5.5m,
        EffortPoints = 13m,
        IsVisibleToExternals = false,
        TicketStatusId = Guid.NewGuid(),
        TicketComplexityId = Guid.NewGuid(),
        TimeUnitId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        AreaId = Guid.NewGuid(),
        ChannelId = Guid.NewGuid(),
        AssignedToUserId = Guid.NewGuid(),
        PrecedentTicketId = Guid.NewGuid()
    };

    private static Ticket CreateValidTicket()
    {
        var ticket = new Ticket
        {
            CompanyId = Guid.NewGuid(),
            Name = "Original",
            Description = "Original description",
            EstimatedTime = 1m,
            ConsumedTime = 2m,
            EffortPoints = 3m,
            IsVisibleToExternals = true,
            TicketStatusId = Guid.NewGuid(),
            TicketComplexityId = Guid.NewGuid(),
            TimeUnitId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            AreaId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            AssignedToUserId = Guid.NewGuid(),
            PrecedentTicketId = Guid.NewGuid(),
            Notifications = [new()],
            ChildTickets = [new Ticket()],
            TicketLogs = [new()]
        };

        SetEntityId(ticket, Guid.NewGuid());
        ticket.SetPersonalizedCode("CRM", 1, 4);
        return ticket;
    }

    private static void SetEntityId(BaseEntity entity, Guid id)
        => typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);
}