using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Queries;

/// <summary>
/// Query used to retrieve a single company module assignment by its identifier within a tenant scope.
/// </summary>
/// <param name="Id">The unique identifier of the requested company module assignment.</param>
/// <param name="CompanyId">The tenant identifier used to scope the result.</param>
public sealed record GetCompanyModulesByIdQuery(Guid Id, Guid CompanyId)
    : IRequest<Response<CompanyModuleDto>>;
