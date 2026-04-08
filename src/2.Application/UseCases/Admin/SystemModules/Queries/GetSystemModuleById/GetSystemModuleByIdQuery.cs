using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.SystemModules.Queries;

/// <summary>
/// Query used to retrieve a single system module by its identifier.
/// </summary>
/// <param name="Id">The unique identifier of the requested system module.</param>
public sealed record GetSystemModuleByIdQuery(Guid Id)
    : IRequest<Response<SystemModuleDto>>;