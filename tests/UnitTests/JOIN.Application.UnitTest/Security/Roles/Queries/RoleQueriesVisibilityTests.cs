using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.DTO.Security.RoleCompany;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.RoleCompanies.Queries.GetRoleCompaniesPaged;
using JOIN.Application.UseCases.Security.RoleCompanies.Queries.GetRoleCompanyById;
using JOIN.Application.UseCases.Security.Roles.Queries.GetRoleById;
using JOIN.Application.UseCases.Security.Roles.Queries.GetRolesDetailed;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.Security.Roles.Queries;

/// <summary>
/// SPEC 41 (Etapa 3) visibility for the repository-backed role queries: the handler resolves
/// <c>includeDeleted</c> through <see cref="SoftDeleteVisibility"/> and the repository only receives
/// <c>true</c> for a SuperAdmin who asked for it.
/// </summary>
public sealed class RoleQueriesVisibilityTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, null, false)]
    public async Task GetRolesDetailed_ShouldForwardIncludeDeletedOnlyForSuperAdmin(bool isSuperAdmin, bool? requested, bool expected)
    {
        var repositoryMock = new Mock<IRoleRepository>();
        repositoryMock
            .Setup(x => x.GetPagedAsync(It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<RoleDto>(), 0));
        var handler = new GetRolesDetailedQueryHandler(repositoryMock.Object, CurrentUser(isSuperAdmin), Options.Create(new PaginationSettings()));

        await handler.Handle(new GetRolesDetailedQuery(null, null, IncludeDeleted: requested), CancellationToken.None);

        repositoryMock.Verify(x => x.GetPagedAsync(It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<int>(), CompanyId, expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public async Task GetRoleById_ShouldForwardIncludeDeletedOnlyForSuperAdmin(bool isSuperAdmin, bool? requested, bool expected)
    {
        var repositoryMock = new Mock<IRoleRepository>();
        var handler = new GetRoleByIdQueryHandler(repositoryMock.Object, CurrentUser(isSuperAdmin));

        await handler.Handle(new GetRoleByIdQuery(Guid.NewGuid(), requested), CancellationToken.None);

        repositoryMock.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), CompanyId, expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public async Task GetRoleCompaniesPaged_ShouldForwardIncludeDeletedOnlyForSuperAdmin(bool isSuperAdmin, bool? requested, bool expected)
    {
        var repositoryMock = new Mock<IRoleCompanyRepository>();
        repositoryMock
            .Setup(x => x.GetPagedAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<RoleCompanyListItemDto>(), 0));
        var handler = new GetRoleCompaniesPagedQueryHandler(repositoryMock.Object, CurrentUser(isSuperAdmin), Options.Create(new PaginationSettings()));

        await handler.Handle(new GetRoleCompaniesPagedQuery(null, null, IncludeDeleted: requested), CancellationToken.None);

        repositoryMock.Verify(x => x.GetPagedAsync(CompanyId, It.IsAny<Guid?>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<int>(), expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public async Task GetRoleCompanyById_ShouldForwardIncludeDeletedOnlyForSuperAdmin(bool isSuperAdmin, bool? requested, bool expected)
    {
        var repositoryMock = new Mock<IRoleCompanyRepository>();
        var handler = new GetRoleCompanyByIdQueryHandler(repositoryMock.Object, CurrentUser(isSuperAdmin));

        await handler.Handle(new GetRoleCompanyByIdQuery(Guid.NewGuid(), IncludeDeleted: requested), CancellationToken.None);

        repositoryMock.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), CompanyId, expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ICurrentUserService CurrentUser(bool isSuperAdmin)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.SetupGet(x => x.CompanyId).Returns(CompanyId);
        mock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
        return mock.Object;
    }
}
