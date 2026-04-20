using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketCompanyDefaults.Commands.UpdateTicketCompanyDefault;

/// <summary>
/// Contains the unit tests for the ticket company default update flow.
/// These tests validate tenant protection, tenant ownership checks,
/// all invalid reference branches from the complex validation method,
/// the persistence failure path, and the successful update scenario.
/// </summary>
public sealed class UpdateTicketCompanyDefaultCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the authenticated user does not provide a valid company context.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyContextIsMissing_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new UpdateTicketCompanyDefaultTestContext(Guid.Empty, isAuthenticated: false);
        var request = CreateValidCommand(_fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");

        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TicketCompanyDefault>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the configuration does not belong to the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenConfigurationDoesNotBelongToCurrentTenant_ShouldReturnNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var requestId = _fixture.Create<Guid>();
        var context = new UpdateTicketCompanyDefaultTestContext(companyId, isAuthenticated: true);

        context.RepositoryMock
            .Setup(x => x.GetAsync(requestId))
            .ReturnsAsync(new TicketCompanyDefault
            {
                CompanyId = _fixture.Create<Guid>(),
                GcRecord = 0
            });

        var request = CreateValidCommand(requestId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_COMPANY_DEFAULT_NOT_FOUND");
        response.Errors.Should().Contain("Ticket company default configuration not found for the current tenant.");

        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TicketCompanyDefault>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies every invalid-reference branch handled by the update validation method.
    /// </summary>
    [Theory]
    [InlineData("Status", "INVALID_TICKET_STATUS", "The provided default ticket status does not exist.")]
    [InlineData("Complexity", "INVALID_TICKET_COMPLEXITY", "The provided default ticket complexity does not exist.")]
    [InlineData("TimeUnit", "INVALID_TIME_UNIT", "The provided default time unit does not exist.")]
    [InlineData("Area", "INVALID_AREA", "The provided default area does not exist for the current tenant.")]
    [InlineData("Project", "INVALID_PROJECT", "The provided default project does not exist for the current tenant.")]
    [InlineData("Channel", "INVALID_CHANNEL", "The provided default communication channel does not exist.")]
    public async Task Handle_WhenReferenceIsInvalid_ShouldReturnValidationError(
        string invalidReference,
        string expectedCode,
        string expectedError)
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var requestId = _fixture.Create<Guid>();
        var context = new UpdateTicketCompanyDefaultTestContext(companyId, isAuthenticated: true);
        var entity = CreateExistingEntity(requestId, companyId);
        var request = CreateCommandForSingleReference(requestId, invalidReference);

        context.RepositoryMock
            .Setup(x => x.GetAsync(requestId))
            .ReturnsAsync(entity);

        ConfigureInvalidReference(context, invalidReference, request);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(expectedCode);
        response.Errors.Should().Contain(expectedError);

        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TicketCompanyDefault>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when the update affects no records.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var requestId = _fixture.Create<Guid>();
        var context = new UpdateTicketCompanyDefaultTestContext(companyId, isAuthenticated: true);
        var entity = CreateExistingEntity(requestId, companyId);
        var request = CreateValidCommand(requestId);

        context.RepositoryMock
            .Setup(x => x.GetAsync(requestId))
            .ReturnsAsync(entity);

        SetupValidReferences(context, request);

        context.MapperMock
            .Setup(x => x.ApplyUpdate(request, entity))
            .Callback(() => ApplyUpdate(request, entity));

        context.RepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPDATE_FAILED");
        response.Errors.Should().Contain("No records were affected while updating the configuration.");

        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies the happy path when the configuration exists and all references are valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateConfigurationAndReturnDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var requestId = _fixture.Create<Guid>();
        var context = new UpdateTicketCompanyDefaultTestContext(companyId, isAuthenticated: true);
        var entity = CreateExistingEntity(requestId, companyId);
        var request = CreateValidCommand(requestId);

        context.RepositoryMock
            .Setup(x => x.GetAsync(requestId))
            .ReturnsAsync(entity);

        SetupValidReferences(context, request);

        context.MapperMock
            .Setup(x => x.ApplyUpdate(request, entity))
            .Callback(() => ApplyUpdate(request, entity));

        context.RepositoryMock
            .Setup(x => x.UpdateAsync(entity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket company default configuration updated successfully.");
        response.Data.Should().NotBeNull();

        entity.StartCode.Should().Be("JOIN");
        entity.CodeSequenceLength.Should().Be(request.CodeSequenceLength);
        entity.UsePersonalizedCode.Should().Be(request.UsePersonalizedCode);

        response.Data!.Id.Should().Be(requestId);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.StartCode.Should().Be("JOIN");
        response.Data.StatusName.Should().Be("Open");
        response.Data.ComplexityName.Should().Be("High");
        response.Data.TimeUnitName.Should().Be("Hours");
        response.Data.AreaName.Should().Be("Support");
        response.Data.ProjectName.Should().Be("CRM Rollout");
        response.Data.ChannelName.Should().Be("Email");

        context.MapperMock.Verify(x => x.ApplyUpdate(request, entity), Times.Once);
        context.RepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid update command covering all reference lookups.
    /// </summary>
    private UpdateTicketCompanyDefaultCommand CreateValidCommand(Guid id)
    {
        return _fixture.Build<UpdateTicketCompanyDefaultCommand>()
            .With(x => x.Id, id)
            .With(x => x.StartCode, "  JOIN  ")
            .With(x => x.CodeSequenceLength, 8)
            .With(x => x.UsePersonalizedCode, true)
            .With(x => x.TicketStatusDefaultId, _fixture.Create<Guid>())
            .With(x => x.TicketComplexityDefaultId, _fixture.Create<Guid>())
            .With(x => x.TimeUnitDefaultId, _fixture.Create<Guid>())
            .With(x => x.AreaDefaultId, _fixture.Create<Guid>())
            .With(x => x.ProjectDefaultId, _fixture.Create<Guid>())
            .With(x => x.ChannelDefaultId, _fixture.Create<Guid>())
            .Create();
    }

    /// <summary>
    /// Creates a command that only exercises a single validation branch.
    /// </summary>
    private UpdateTicketCompanyDefaultCommand CreateCommandForSingleReference(Guid id, string invalidReference)
    {
        var statusId = invalidReference == "Status" ? _fixture.Create<Guid>() : (Guid?)null;
        var complexityId = invalidReference == "Complexity" ? _fixture.Create<Guid>() : (Guid?)null;
        var timeUnitId = invalidReference == "TimeUnit" ? _fixture.Create<Guid>() : (Guid?)null;
        var areaId = invalidReference == "Area" ? _fixture.Create<Guid>() : (Guid?)null;
        var projectId = invalidReference == "Project" ? _fixture.Create<Guid>() : (Guid?)null;
        var channelId = invalidReference == "Channel" ? _fixture.Create<Guid>() : (Guid?)null;

        return _fixture.Build<UpdateTicketCompanyDefaultCommand>()
            .With(x => x.Id, id)
            .With(x => x.StartCode, "  TCK  ")
            .With(x => x.CodeSequenceLength, 5)
            .With(x => x.UsePersonalizedCode, true)
            .With(x => x.TicketStatusDefaultId, statusId)
            .With(x => x.TicketComplexityDefaultId, complexityId)
            .With(x => x.TimeUnitDefaultId, timeUnitId)
            .With(x => x.AreaDefaultId, areaId)
            .With(x => x.ProjectDefaultId, projectId)
            .With(x => x.ChannelDefaultId, channelId)
            .Create();
    }

    /// <summary>
    /// Configures the targeted lookup to return null and trigger the intended validation error.
    /// </summary>
    private static void ConfigureInvalidReference(
        UpdateTicketCompanyDefaultTestContext context,
        string invalidReference,
        UpdateTicketCompanyDefaultCommand request)
    {
        switch (invalidReference)
        {
            case "Status":
                context.TicketStatusRepositoryMock
                    .Setup(x => x.GetAsync(request.TicketStatusDefaultId!.Value))
                    .ReturnsAsync((TicketStatus?)null);
                break;

            case "Complexity":
                context.TicketComplexityRepositoryMock
                    .Setup(x => x.GetAsync(request.TicketComplexityDefaultId!.Value))
                    .ReturnsAsync((TicketComplexity?)null);
                break;

            case "TimeUnit":
                context.TimeUnitRepositoryMock
                    .Setup(x => x.GetAsync(request.TimeUnitDefaultId!.Value))
                    .ReturnsAsync((TimeUnit?)null);
                break;

            case "Area":
                context.AreaRepositoryMock
                    .Setup(x => x.GetAsync(request.AreaDefaultId!.Value))
                    .ReturnsAsync((Area?)null);
                break;

            case "Project":
                context.ProjectRepositoryMock
                    .Setup(x => x.GetAsync(request.ProjectDefaultId!.Value))
                    .ReturnsAsync((Project?)null);
                break;

            case "Channel":
                context.ChannelRepositoryMock
                    .Setup(x => x.GetAsync(request.ChannelDefaultId!.Value))
                    .ReturnsAsync((CommunicationChannel?)null);
                break;
        }
    }

    /// <summary>
    /// Configures all reference repositories to return valid rows.
    /// </summary>
    private static void SetupValidReferences(
        UpdateTicketCompanyDefaultTestContext context,
        UpdateTicketCompanyDefaultCommand request)
    {
        context.TicketStatusRepositoryMock
            .Setup(x => x.GetAsync(request.TicketStatusDefaultId!.Value))
            .ReturnsAsync(new TicketStatus { Name = "Open" });

        context.TicketComplexityRepositoryMock
            .Setup(x => x.GetAsync(request.TicketComplexityDefaultId!.Value))
            .ReturnsAsync(new TicketComplexity { Name = "High" });

        context.TimeUnitRepositoryMock
            .Setup(x => x.GetAsync(request.TimeUnitDefaultId!.Value))
            .ReturnsAsync(new TimeUnit { Name = "Hours" });

        context.AreaRepositoryMock
            .Setup(x => x.GetAsync(request.AreaDefaultId!.Value))
            .ReturnsAsync(new Area { Name = "Support" });

        context.ProjectRepositoryMock
            .Setup(x => x.GetAsync(request.ProjectDefaultId!.Value))
            .ReturnsAsync(new Project { Name = "CRM Rollout" });

        context.ChannelRepositoryMock
            .Setup(x => x.GetAsync(request.ChannelDefaultId!.Value))
            .ReturnsAsync(new CommunicationChannel { Name = "Email" });
    }

    /// <summary>
    /// Applies the update payload to the tracked entity, mirroring the mapper behavior needed by the handler.
    /// </summary>
    private static void ApplyUpdate(UpdateTicketCompanyDefaultCommand request, TicketCompanyDefault entity)
    {
        entity.StartCode = request.StartCode;
        entity.CodeSequenceLength = request.CodeSequenceLength;
        entity.UsePersonalizedCode = request.UsePersonalizedCode;
        entity.TicketStatusDefaultId = request.TicketStatusDefaultId;
        entity.TicketComplexityDefaultId = request.TicketComplexityDefaultId;
        entity.TimeUnitDefaultId = request.TimeUnitDefaultId;
        entity.AreaDefaultId = request.AreaDefaultId;
        entity.ProjectDefaultId = request.ProjectDefaultId;
        entity.ChannelDefaultId = request.ChannelDefaultId;
    }

    /// <summary>
    /// Creates an existing entity owned by the given company.
    /// </summary>
    private static TicketCompanyDefault CreateExistingEntity(Guid id, Guid companyId)
    {
        var entity = new TicketCompanyDefault
        {
            CompanyId = companyId,
            GcRecord = 0,
            StartCode = "OLD",
            CodeSequenceLength = 4,
            UsePersonalizedCode = false,
            Created = new DateTime(2026, 4, 19, 9, 0, 0, DateTimeKind.Utc)
        };

        typeof(JOIN.Domain.Audit.BaseEntity)
            .GetProperty("Id")!
            .SetValue(entity, id);

        return entity;
    }

    /// <summary>
    /// Creates a generic repository mock to reduce arrange noise.
    /// </summary>
    private static Mock<IGenericRepository<TEntity>> CreateRepositoryMock<TEntity>()
        where TEntity : class
    {
        return new Mock<IGenericRepository<TEntity>>();
    }

    /// <summary>
    /// Registers a generic repository in the mocked unit of work.
    /// </summary>
    private static void SetupRepository<TEntity>(
        Mock<IUnitOfWork> unitOfWorkMock,
        Mock<IGenericRepository<TEntity>> repositoryMock)
        where TEntity : class
    {
        unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
    }

    /// <summary>
    /// Holds the reusable mocks and factory helper for the update handler tests.
    /// </summary>
    private sealed class UpdateTicketCompanyDefaultTestContext
    {
        public UpdateTicketCompanyDefaultTestContext(Guid companyId, bool isAuthenticated)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(isAuthenticated);

            SetupRepository(UnitOfWorkMock, RepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketStatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketComplexityRepositoryMock);
            SetupRepository(UnitOfWorkMock, TimeUnitRepositoryMock);
            SetupRepository(UnitOfWorkMock, AreaRepositoryMock);
            SetupRepository(UnitOfWorkMock, ProjectRepositoryMock);
            SetupRepository(UnitOfWorkMock, ChannelRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ITicketCompanyDefaultMapper> MapperMock { get; } = new();
        public Mock<IGenericRepository<TicketCompanyDefault>> RepositoryMock { get; } = CreateRepositoryMock<TicketCompanyDefault>();
        public Mock<IGenericRepository<TicketStatus>> TicketStatusRepositoryMock { get; } = CreateRepositoryMock<TicketStatus>();
        public Mock<IGenericRepository<TicketComplexity>> TicketComplexityRepositoryMock { get; } = CreateRepositoryMock<TicketComplexity>();
        public Mock<IGenericRepository<TimeUnit>> TimeUnitRepositoryMock { get; } = CreateRepositoryMock<TimeUnit>();
        public Mock<IGenericRepository<Area>> AreaRepositoryMock { get; } = CreateRepositoryMock<Area>();
        public Mock<IGenericRepository<Project>> ProjectRepositoryMock { get; } = CreateRepositoryMock<Project>();
        public Mock<IGenericRepository<CommunicationChannel>> ChannelRepositoryMock { get; } = CreateRepositoryMock<CommunicationChannel>();

        public UpdateTicketCompanyDefaultCommandHandler CreateHandler()
        {
            return new UpdateTicketCompanyDefaultCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                MapperMock.Object);
        }
    }
}
