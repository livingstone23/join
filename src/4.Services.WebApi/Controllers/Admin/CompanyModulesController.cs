using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.CompanyModules.Commands;
using JOIN.Application.UseCases.Admin.CompanyModules.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Admin;

/// <summary>
/// Exposes REST endpoints for managing tenant-scoped company module assignments.
/// The controller remains intentionally thin and delegates all business rules and persistence concerns to the Application layer through MediatR.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("CompanyModules")]
public class CompanyModulesController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// Retrieves a paginated list of company module assignments for the tenant identified by the <c>X-Company-Id</c> header.
    /// Optional filters can be applied by company name, module name, and inclusive creation-date range.
    /// </summary>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="pageNumber">Optional page number. When omitted, the configured default value is used.</param>
    /// <param name="pageSize">Optional page size. When omitted, the configured default value is used.</param>
    /// <param name="companyName">Optional partial-match filter applied to the company name.</param>
    /// <param name="moduleName">Optional partial-match filter applied to the system module name.</param>
    /// <param name="createdFrom">Optional inclusive lower bound used to filter by creation date.</param>
    /// <param name="createdTo">Optional inclusive upper bound used to filter by creation date.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized paged response containing the matching company module assignments.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CompanyModuleListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAll(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? companyName = null,
        [FromQuery] string? moduleName = null,
        [FromQuery] DateTime? createdFrom = null,
        [FromQuery] DateTime? createdTo = null,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<PagedResult<CompanyModuleListItemDto>>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var response = await _sender.Send(
            new GetCompanyModulesQuery(companyId, pageNumber, pageSize, companyName, moduleName, createdFrom, createdTo),
            cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a single company module assignment by its unique identifier within the tenant identified by the <c>X-Company-Id</c> header.
    /// </summary>
    /// <param name="id">The unique identifier of the assignment to retrieve.</param>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized response containing the requested assignment when it exists.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CompanyModuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<CompanyModuleDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var response = await _sender.Send(new GetCompanyModulesByIdQuery(id, companyId), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_MODULE_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new company module assignment within the tenant identified by the <c>X-Company-Id</c> header.
    /// </summary>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="command">The creation payload containing the assignment data to persist.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the create command is being processed.</param>
    /// <returns>A <c>201 Created</c> response containing the newly created assignment resource.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CompanyModuleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] CreateCompanyModulesCommand command,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<CompanyModuleDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var request = command with { CompanyId = companyId };
        var response = await _sender.Send(request, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_MODULE_ALREADY_EXISTS")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response);
    }

    /// <summary>
    /// Updates an existing company module assignment using the route identifier as the authoritative resource key and the <c>X-Company-Id</c> header as the tenant scope.
    /// </summary>
    /// <param name="id">The unique identifier of the assignment to update.</param>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="command">The update payload containing the desired assignment state.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command is being processed.</param>
    /// <returns>A standardized response containing the updated assignment resource.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CompanyModuleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] UpdateCompanyModulesCommand command,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<CompanyModuleDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var request = command with { Id = id, CompanyId = companyId };
        var response = await _sender.Send(request, cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_MODULE_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete over a company module assignment that belongs to the tenant identified by the <c>X-Company-Id</c> header.
    /// </summary>
    /// <param name="id">The unique identifier of the assignment to delete.</param>
    /// <param name="companyId">The tenant identifier extracted from the <c>X-Company-Id</c> header.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the delete command is being processed.</param>
    /// <returns>A standardized response containing the deleted assignment identifier.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return BadRequest(Response<Guid>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]));
        }

        var response = await _sender.Send(new DeleteCompanyModulesCommand(id, companyId), cancellationToken);
        if (!response.IsSuccess)
        {
            if (response.Message == "COMPANY_MODULE_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
