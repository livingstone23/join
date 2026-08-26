// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Audit;
using JOIN.Application.UseCases.Security.Audit.Queries.GetSecurityAuditLog;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Security;

/// <summary>
/// SPEC 29 — read-only endpoint that exposes the <c>Security.AuditLogs</c> bitácora.
/// Every mutating handler in the security surface (Roles, RoleCompanies, RoleSystemOptions,
/// Users, UserRoleCompanies, UserCompanies) writes a row through <c>IAuditLogger</c>; this
/// endpoint is the only way the front can read them back.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("Audit")]
public class AuditController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Returns a page of bitácora rows. Tenant-scoped by the JWT's <c>CompanyId</c>.
    /// SuperAdmin callers may pass <c>allTenants=true</c> to span every tenant; the
    /// flag is silently ignored for everyone else.
    /// </summary>
    [HttpGet("security")]
    [Authorize]
    [ProducesResponseType(typeof(Response<PagedResult<SecurityAuditLogItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Response<PagedResult<SecurityAuditLogItemDto>>>> GetSecurityLog(
        [FromQuery] string? entity,
        [FromQuery] Guid? entityId,
        [FromQuery] string? changedBy,
        [FromQuery] string? action,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool allTenants = false,
        CancellationToken cancellationToken = default)
    {
        var query = new GetSecurityAuditLogQuery(
            Entity: entity,
            EntityId: entityId,
            ChangedBy: changedBy,
            Action: action,
            FromDate: fromDate,
            ToDate: toDate,
            PageNumber: pageNumber,
            PageSize: pageSize,
            AllTenants: allTenants);

        var result = await _mediator.Send(query, cancellationToken);

        // The handler returns Response<T> with IsSuccess=false and a code. Map
        // domain codes to HTTP status per SPEC 29 F10 step 2.
        if (result.IsSuccess)
        {
            return Ok(result);
        }

        var code = result.Errors.FirstOrDefault();
        return code switch
        {
            "TENANT_REQUIRED" => BadRequest(result),
            "INVALID_ENTITY" => BadRequest(result),
            "INVALID_ACTION" => BadRequest(result),
            _ => BadRequest(result)
        };
    }
}
