using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Security.UserCompanies.Commands.SetDefaultCompany;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.UserCompanies.Commands.SetDefaultCompany;

/// <summary>
/// Contains the unit tests for the default-company switching flow.
/// </summary>
public sealed class SetDefaultCompanyCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the error returned when the user has no company assignments.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasNoAssignments_ShouldReturnUserCompanyNotFoundError()
    {
        var context = new SetDefaultCompanyCommandTestContext();
        var request = new SetDefaultCompanyCommand(_fixture.Create<Guid>(), _fixture.Create<Guid>());

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserCompany>());

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_COMPANY_NOT_FOUND");
        response.Errors.Should().Contain("The user does not have any active company assignments.");

        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserCompany>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the error returned when the target company is not linked to the user.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTargetCompanyIsNotAssigned_ShouldReturnTargetNotAssignedError()
    {
        var context = new SetDefaultCompanyCommandTestContext();
        var userId = _fixture.Create<Guid>();
        var request = new SetDefaultCompanyCommand(userId, _fixture.Create<Guid>());

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new UserCompany
                {
                    UserId = userId,
                    CompanyId = _fixture.Create<Guid>(),
                    IsDefault = true
                }
            });

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TARGET_COMPANY_NOT_ASSIGNED");
        response.Errors.Should().Contain("The selected company is not assigned to the user.");

        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserCompany>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the handler clears the previous default and promotes the target company.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSwitchingDefaultCompany_ShouldUpdateAssignmentsAndSaveOnce()
    {
        var context = new SetDefaultCompanyCommandTestContext();
        var userId = _fixture.Create<Guid>();
        var oldCompanyId = _fixture.Create<Guid>();
        var newCompanyId = _fixture.Create<Guid>();

        var currentDefault = new UserCompany
        {
            UserId = userId,
            CompanyId = oldCompanyId,
            IsDefault = true
        };

        var targetLink = new UserCompany
        {
            UserId = userId,
            CompanyId = newCompanyId,
            IsDefault = false
        };

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { currentDefault, targetLink });

        var handler = context.CreateHandler();
        var response = await handler.Handle(new SetDefaultCompanyCommand(userId, newCompanyId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Default company updated successfully.");
        response.Data.Should().Be(newCompanyId);

        currentDefault.IsDefault.Should().BeFalse();
        targetLink.IsDefault.Should().BeTrue();

        context.RepositoryMock.Verify(x => x.UpdateAsync(currentDefault), Times.Once);
        context.RepositoryMock.Verify(x => x.UpdateAsync(targetLink), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies the no-op success path when the selected company is already the default.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTargetAlreadyIsDefault_ShouldReturnSuccessWithoutSaving()
    {
        var context = new SetDefaultCompanyCommandTestContext();
        var userId = _fixture.Create<Guid>();
        var companyId = _fixture.Create<Guid>();

        var existingDefault = new UserCompany
        {
            UserId = userId,
            CompanyId = companyId,
            IsDefault = true
        };

        context.RepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { existingDefault });

        var handler = context.CreateHandler();
        var response = await handler.Handle(new SetDefaultCompanyCommand(userId, companyId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Default company updated successfully.");
        response.Data.Should().Be(companyId);

        context.RepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<UserCompany>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Holds the reusable mocks for the handler tests.
    /// </summary>
    private sealed class SetDefaultCompanyCommandTestContext
    {
        public SetDefaultCompanyCommandTestContext()
        {
            RepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<UserCompany>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<UserCompany>()).Returns(RepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<UserCompany>> RepositoryMock { get; } = new();

        public SetDefaultCompanyCommandHandler CreateHandler()
        {
            return new SetDefaultCompanyCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
