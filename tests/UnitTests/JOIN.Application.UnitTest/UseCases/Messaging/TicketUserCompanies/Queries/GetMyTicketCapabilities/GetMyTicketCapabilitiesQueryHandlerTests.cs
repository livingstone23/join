using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetMyTicketCapabilities;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Queries.GetMyTicketCapabilities;

/// <summary>
/// Unit tests for <see cref="GetMyTicketCapabilitiesQueryHandler"/> (SPEC 53).
/// Mirrors the contract of <see cref="TicketUserCompanyCapabilityResolver.ResolveAsync"/>
/// so the four flags the front receives are the same ones <c>FinishTicket</c>
/// and <c>ReassignTicket</c> validate against.
/// </summary>
public sealed class GetMyTicketCapabilitiesQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new TestContext(companyId: Guid.Empty, userId: Guid.NewGuid().ToString());

        var response = await context.CreateHandler().Handle(new GetMyTicketCapabilitiesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenUserIdIsMissingOrUnparsable_ShouldReturnUserRequiredError()
    {
        var context = new TestContext(companyId: _fixture.Create<Guid>(), userId: null);

        var response = await context.CreateHandler().Handle(new GetMyTicketCapabilitiesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsOnRoster_ShouldReturnFlagsVerbatim()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new TestContext(companyId, userId.ToString());
        context.SeedRoster(new TicketUserCompany
        {
            CompanyId = companyId,
            UserId = userId,
            IsSuperAdminTicket = true,
            CanFinishTicket = true,
            CanResolveTicket = true,
            GcRecord = 0
        });

        var response = await context.CreateHandler().Handle(new GetMyTicketCapabilitiesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.IsMember.Should().BeTrue();
        response.Data.IsSuperAdminTicket.Should().BeTrue();
        response.Data.CanFinishTicket.Should().BeTrue();
        response.Data.CanResolveTicket.Should().BeTrue();
    }

    /// <summary>
    /// Out-of-roster caller — front renders no capability-gated actions; resolver
    /// returns <c>default(TicketUserCompanyCapabilityWithMembership)</c> which
    /// projects to <c>IsMember = false</c> and every capability <c>false</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCallerIsNotOnRoster_ShouldReturnAllFalseButIsMemberFalse()
    {
        var companyId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new TestContext(companyId, userId.ToString());

        var response = await context.CreateHandler().Handle(new GetMyTicketCapabilitiesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.IsMember.Should().BeFalse();
        response.Data.IsSuperAdminTicket.Should().BeFalse();
        response.Data.CanFinishTicket.Should().BeFalse();
        response.Data.CanResolveTicket.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenNonSuperAdminRequestsAnotherCompany_ShouldStillUseTokenCompany()
    {
        var tokenCompany = _fixture.Create<Guid>();
        var otherCompany = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new TestContext(tokenCompany, userId.ToString(), isSuperAdmin: false);
        context.SeedRoster(new TicketUserCompany
        {
            CompanyId = tokenCompany,
            UserId = userId,
            IsSuperAdminTicket = false,
            CanFinishTicket = false,
            CanResolveTicket = true,
            GcRecord = 0
        });

        var response = await context.CreateHandler().Handle(new GetMyTicketCapabilitiesQuery(CompanyId: otherCompany), CancellationToken.None);

        // The handler must have queried the token company, not the requested one.
        response.Data!.IsMember.Should().BeTrue();
        response.Data.CanResolveTicket.Should().BeTrue();
        // And the roster lookup must have been asked exactly once with the token company.
        context.RepositoryMock.Verify(x => x.GetAllAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSuperAdminTargetsAnotherCompany_ShouldQueryThatCompany()
    {
        var tokenCompany = _fixture.Create<Guid>();
        var targetCompany = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new TestContext(tokenCompany, userId.ToString(), isSuperAdmin: true);
        context.SeedRoster(new TicketUserCompany
        {
            CompanyId = targetCompany,
            UserId = userId,
            IsSuperAdminTicket = false,
            CanFinishTicket = false,
            CanResolveTicket = true,
            GcRecord = 0
        });

        var response = await context.CreateHandler().Handle(new GetMyTicketCapabilitiesQuery(CompanyId: targetCompany), CancellationToken.None);

        response.Data!.IsMember.Should().BeTrue();
        response.Data.CanResolveTicket.Should().BeTrue();
    }

    private sealed class TestContext
    {
        public TestContext(Guid companyId, string? userId, bool isSuperAdmin = false)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId);
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(!string.IsNullOrEmpty(userId));
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketUserCompany>()).Returns(RepositoryMock.Object);
            RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketUserCompany>());
        }

        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<TicketUserCompany>> RepositoryMock { get; } = new();

        /// <summary>
        /// The real resolver wires the UnitOfWork → repository lookup; tests seed
        /// the roster through <see cref="RepositoryMock"/> (Moq cannot mock the
        /// sealed resolver itself).
        /// </summary>
        public GetMyTicketCapabilitiesQueryHandler CreateHandler()
        {
            var resolver = new TicketUserCompanyCapabilityResolver(UnitOfWorkMock.Object);
            return new GetMyTicketCapabilitiesQueryHandler(CurrentUserServiceMock.Object, resolver);
        }

        public void SeedRoster(params TicketUserCompany[] rows)
            => RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(rows);
    }
}