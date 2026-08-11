// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security.RoleCompany;
using JOIN.Application.UseCases.Security.RoleCompanies.Commands.CreateRoleCompany;
using JOIN.Application.UseCases.Security.RoleCompanies.Commands.DeleteRoleCompany;
using JOIN.Application.UseCases.Security.RoleCompanies.Commands.UpdateRoleCompany;
using JOIN.Application.UseCases.Security.RoleCompanies.Queries.GetRoleCompanyById;
using JOIN.Application.UseCases.Security.RoleCompanies.Queries.GetRoleCompaniesPaged;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;



namespace JOIN.Services.WebApi.Controllers.Security;



/// <summary>
/// Exposes the RoleCompany junction endpoints used by a SuperAdminCompany to decide which roles
/// their tenant may assign to users. All handlers resolve <c>CompanyId</c> exclusively from the
/// caller's token; the body / query string never carries a tenant identifier.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("RoleCompanies")]
public class RoleCompaniesController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Returns the detailed projection of a single RoleCompany link, scoped to the caller's tenant.
    /// </summary>
    /// <param name="id">Unique identifier of the RoleCompany junction row.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// <c>200 OK</c> with the DTO; <c>404 Not Found</c> when the link is missing, soft-deleted,
    /// or belongs to another tenant (cross-tenant reads are intentionally indistinguishable from missing).
    /// </returns>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdminCompany")]
    [ProducesResponseType(typeof(Response<RoleCompanyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Response<RoleCompanyDto>>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetRoleCompanyByIdQuery(id), cancellationToken);
        return MapGetByIdResponse(response);
    }

    /// <summary>
    /// Returns a paged, filtered view of RoleCompany rows for the caller's tenant.
    /// </summary>
    /// <param name="roleId">Optional RoleId filter (exact match).</param>
    /// <param name="isActive">Optional active flag (true = GcRecord = 0; false = GcRecord != 0; null = all).</param>
    /// <param name="page">1-based page number; clamped to &gt;= 1.</param>
    /// <param name="pageSize">Page size; clamped to [1, 100].</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    [HttpGet]
    [Authorize(Roles = "SuperAdminCompany")]
    [ProducesResponseType(typeof(Response<PagedResult<RoleCompanyListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<Response<PagedResult<RoleCompanyListItemDto>>>> GetPaged(
        [FromQuery] Guid? roleId,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetRoleCompaniesPagedQuery(roleId, isActive, page, pageSize),
            cancellationToken);

        return MapMutationOrQueryResponse(response, successStatus: StatusCodes.Status200OK);
    }

    /// <summary>
    /// Creates a new RoleCompany link. <c>CompanyId</c> is taken from the caller's token, never the body.
    /// </summary>
    /// <param name="request">Payload with the RoleId to link.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// <c>201 Created</c> with the DTO and a <c>Location</c> header pointing to the new resource;
    /// <c>400 Bad Request</c> when the RoleId is missing, unknown, or soft-deleted;
    /// <c>409 Conflict</c> when an active link for the same (Role, Company) already exists.
    /// </returns>
    [HttpPost]
    [Authorize(Roles = "SuperAdminCompany")]
    [ProducesResponseType(typeof(Response<RoleCompanyDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Response<RoleCompanyDto>>> Create(
        [FromBody] CreateRoleCompanyRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new CreateRoleCompanyCommand(request.RoleId), cancellationToken);
        if (response.IsSuccess && response.Data is not null)
        {
            return CreatedAtAction(nameof(GetById), new { id = response.Data.Id }, response);
        }

        return MapMutationOrQueryResponse(response, successStatus: StatusCodes.Status201Created);
    }

    /// <summary>
    /// Reassigns the RoleId of an existing RoleCompany link. <c>CompanyId</c> is preserved from the token.
    /// </summary>
    /// <param name="id">Unique identifier of the RoleCompany link to update.</param>
    /// <param name="request">Payload with the new RoleId.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// <c>200 OK</c> with the updated DTO;
    /// <c>400 Bad Request</c> when the new RoleId is missing, unknown, or soft-deleted;
    /// <c>404 Not Found</c> when the link is missing, soft-deleted, or cross-tenant;
    /// <c>409 Conflict</c> when the new RoleId already has an active link in the same tenant.
    /// </returns>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdminCompany")]
    [ProducesResponseType(typeof(Response<RoleCompanyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Response<RoleCompanyDto>>> Update(
        Guid id,
        [FromBody] UpdateRoleCompanyRequestDto request,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new UpdateRoleCompanyCommand(id, request.RoleId), cancellationToken);
        return MapMutationOrQueryResponse(response, successStatus: StatusCodes.Status200OK);
    }

    /// <summary>
    /// Soft-deletes a RoleCompany link (stamps GcRecord with the yyyyMMdd UTC int).
    /// </summary>
    /// <param name="id">Unique identifier of the RoleCompany link to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// <c>204 No Content</c> on success;
    /// <c>404 Not Found</c> when the link is missing, soft-deleted, or cross-tenant.
    /// </returns>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdminCompany")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new DeleteRoleCompanyCommand(id), cancellationToken);
        if (response.IsSuccess)
        {
            return NoContent();
        }

        return MapErrorResponse(response);
    }

    // ----- Response mapping helpers -----
    // Handlers encode outcomes as standardized Message codes; controllers translate them to HTTP statuses.

    private static ActionResult<Response<RoleCompanyDto>> MapGetByIdResponse(Response<RoleCompanyDto> response)
    {
        if (response.IsSuccess)
        {
            return new ObjectResult(response) { StatusCode = StatusCodes.Status200OK };
        }

        return MapErrorResponse(response);
    }

    private static ActionResult MapMutationOrQueryResponse<T>(Response<T> response, int successStatus)
    {
        if (response.IsSuccess)
        {
            return new ObjectResult(response) { StatusCode = successStatus };
        }

        return MapErrorResponse(response);
    }

    private static ActionResult MapErrorResponse<T>(Response<T> response)
    {
        var status = response.Message switch
        {
            "INVALID_COMPANY_ID" => StatusCodes.Status401Unauthorized,
            "ROLE_COMPANY_NOT_FOUND" => StatusCodes.Status404NotFound,
            "ROLE_COMPANY_DUPLICATE" => StatusCodes.Status409Conflict,
            "ROLE_NOT_FOUND" => StatusCodes.Status400BadRequest,
            "ROLE_INACTIVE" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status400BadRequest
        };

        return new ObjectResult(response) { StatusCode = status };
    }
}
