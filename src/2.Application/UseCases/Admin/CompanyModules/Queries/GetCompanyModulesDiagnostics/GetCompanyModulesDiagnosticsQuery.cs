using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Queries;

/// <summary>
/// SPEC 48 F: returns the diagnostics payload for the SuperAdmin
/// <c>GET /CompanyModules/diagnostics</c> endpoint. Read-only.
/// </summary>
public sealed record GetCompanyModulesDiagnosticsQuery
    : IRequest<Response<CompanyModulesDiagnosticsDto>>;