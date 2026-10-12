using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets;

/// <summary>
/// Unit tests for <see cref="TicketInitialStatusResolver"/> (SPEC 42): a status sent by the client
/// must be active, not final and belong to the company; without one, the company's
/// <c>TicketCompanyDefaults.TicketStatusDefaultId</c> applies under the same rules.
/// </summary>
public sealed class TicketInitialStatusResolverTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<IGenericRepository<TicketStatus>> _statusRepositoryMock = new();
    private readonly Mock<IGenericRepository<TicketCompanyDefault>> _defaultsRepositoryMock = new();

    public TicketInitialStatusResolverTests()
    {
        _unitOfWorkMock.Setup(x => x.GetRepository<TicketStatus>()).Returns(_statusRepositoryMock.Object);
        _unitOfWorkMock.Setup(x => x.GetRepository<TicketCompanyDefault>()).Returns(_defaultsRepositoryMock.Object);
        _defaultsRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketCompanyDefault>());
    }

    public enum StatusDefect
    {
        Missing,
        Deleted,
        Inactive,
        Final,
        OtherCompany
    }

    [Fact]
    public async Task ResolveAsync_WhenRequestedStatusIsUsable_ShouldReturnIt()
    {
        var status = UsableStatus();
        _statusRepositoryMock.Setup(x => x.GetAsync(status.Id)).ReturnsAsync(status);

        var result = await Resolve(status.Id);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(status.Id);
        _defaultsRepositoryMock.Verify(x => x.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task ResolveAsync_WhenRequestedStatusIsNotTheInitialOne_ShouldStillAcceptIt()
    {
        var inProgress = UsableStatus();
        inProgress.IsInitial = false;
        _statusRepositoryMock.Setup(x => x.GetAsync(inProgress.Id)).ReturnsAsync(inProgress);

        var result = await Resolve(inProgress.Id);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(inProgress.Id);
    }

    [Theory]
    [InlineData(StatusDefect.Missing)]
    [InlineData(StatusDefect.Deleted)]
    [InlineData(StatusDefect.Inactive)]
    [InlineData(StatusDefect.Final)]
    [InlineData(StatusDefect.OtherCompany)]
    public async Task ResolveAsync_WhenRequestedStatusIsNotUsable_ShouldReturnInvalidTicketStatus(StatusDefect defect)
    {
        var requestedId = SetupStatusWith(defect);

        var result = await Resolve(requestedId);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("INVALID_TICKET_STATUS");
        result.Errors.Should().Contain("The ticket status must be active, not final and belong to the current company.");
    }

    [Fact]
    public async Task ResolveAsync_WhenNoStatusAndNoDefaultsRow_ShouldReturnNotConfigured()
    {
        var result = await Resolve(null);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("TICKET_DEFAULT_STATUS_NOT_CONFIGURED");
    }

    [Fact]
    public async Task ResolveAsync_WhenNoStatusAndDefaultsRowHasNoStatus_ShouldReturnNotConfigured()
    {
        SetupDefaults(new TicketCompanyDefault { CompanyId = CompanyId, TicketStatusDefaultId = null });

        var result = await Resolve(null);

        result.Message.Should().Be("TICKET_DEFAULT_STATUS_NOT_CONFIGURED");
    }

    [Fact]
    public async Task ResolveAsync_WhenOnlyDeletedOrForeignDefaultsRowsExist_ShouldReturnNotConfigured()
    {
        var deleted = new TicketCompanyDefault { CompanyId = CompanyId, TicketStatusDefaultId = Guid.NewGuid() };
        deleted.MarkAsDeleted();
        SetupDefaults(deleted, new TicketCompanyDefault { CompanyId = Guid.NewGuid(), TicketStatusDefaultId = Guid.NewGuid() });

        var result = await Resolve(null);

        result.Message.Should().Be("TICKET_DEFAULT_STATUS_NOT_CONFIGURED");
        _statusRepositoryMock.Verify(x => x.GetAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Theory]
    [InlineData(StatusDefect.Missing)]
    [InlineData(StatusDefect.Deleted)]
    [InlineData(StatusDefect.Inactive)]
    [InlineData(StatusDefect.Final)]
    [InlineData(StatusDefect.OtherCompany)]
    public async Task ResolveAsync_WhenNoStatusAndConfiguredStatusIsNotUsable_ShouldReturnDefaultInvalid(StatusDefect defect)
    {
        var configuredId = SetupStatusWith(defect);
        SetupDefaults(new TicketCompanyDefault { CompanyId = CompanyId, TicketStatusDefaultId = configuredId });

        var result = await Resolve(null);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("TICKET_DEFAULT_STATUS_INVALID");
    }

    [Fact]
    public async Task ResolveAsync_WhenNoStatusAndConfiguredStatusIsUsable_ShouldReturnIt()
    {
        var configured = UsableStatus();
        SetupDefaults(new TicketCompanyDefault { CompanyId = CompanyId, TicketStatusDefaultId = configured.Id });
        _statusRepositoryMock.Setup(x => x.GetAsync(configured.Id)).ReturnsAsync(configured);

        var result = await Resolve(null);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Be(configured.Id);
    }

    private Task<JOIN.Application.Common.Response<Guid>> Resolve(Guid? requestedStatusId) =>
        new TicketInitialStatusResolver(_unitOfWorkMock.Object).ResolveAsync(CompanyId, requestedStatusId, CancellationToken.None);

    private void SetupDefaults(params TicketCompanyDefault[] rows) =>
        _defaultsRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(rows);

    private static TicketStatus UsableStatus() =>
        new() { Name = "Abierto", CompanyId = CompanyId, IsActive = true, IsInitial = true, IsFinal = false };

    /// <summary>
    /// Registers a status carrying <paramref name="defect"/> and returns the id to look it up by
    /// (an unknown id for <see cref="StatusDefect.Missing"/>).
    /// </summary>
    private Guid SetupStatusWith(StatusDefect defect)
    {
        if (defect == StatusDefect.Missing)
        {
            var unknownId = Guid.NewGuid();
            _statusRepositoryMock.Setup(x => x.GetAsync(unknownId)).ReturnsAsync((TicketStatus?)null);
            return unknownId;
        }

        var status = UsableStatus();
        switch (defect)
        {
            case StatusDefect.Deleted:
                status.MarkAsDeleted();
                break;
            case StatusDefect.Inactive:
                status.IsActive = false;
                break;
            case StatusDefect.Final:
                status.IsFinal = true;
                break;
            case StatusDefect.OtherCompany:
                status.CompanyId = Guid.NewGuid();
                break;
        }

        _statusRepositoryMock.Setup(x => x.GetAsync(status.Id)).ReturnsAsync(status);
        return status.Id;
    }
}
