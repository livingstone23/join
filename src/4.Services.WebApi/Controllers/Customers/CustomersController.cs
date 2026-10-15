using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;
using JOIN.Application.UseCases.Customers.Customers.Commands;
using JOIN.Application.UseCases.Customers.Customers.Queries;
using JOIN.Domain.Customers;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using JOIN.Domain.Security;

namespace JOIN.Services.WebApi.Controllers.Customers;

/// <summary>
/// Exposes REST endpoints for tenant-scoped customers (SPEC 55): one customer per person and company, never
/// deleted, only active or inactive, with a status history.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("Customers")]
public class CustomersController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CustomerResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? customerCode = null,
        [FromQuery] Guid? customerStageId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? personName = null,
        [FromQuery] string? userEmail = null,
        [FromQuery] bool? includeDeleted = null,
        [FromQuery] Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetCustomersPagedQuery(pageNumber, pageSize, customerCode, customerStageId, isActive, personName, userEmail, includeDeleted, companyId),
            cancellationToken);

        return this.ToResult(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CustomerResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] bool? includeDeleted = null, [FromQuery] Guid? companyId = null, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCustomerByIdQuery(id, includeDeleted, companyId), cancellationToken);
        return this.ToResult(response);
    }

    /// <summary>
    /// Makes the person an active customer (idempotent): 201 when the customer is created, 200 with the existing
    /// customer when it is reactivated (<c>Activated</c>) or was already active (<c>Confirmed</c>).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CustomerResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<CustomerResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(command, cancellationToken);

        return response.IsSuccess && response.Message == nameof(CustomerStatusAction.Created)
            ? this.ToCreated(response)
            : this.ToResult(response);
    }

    /// <summary>
    /// Changes the customer's stage. The active state is changed only by activate/deactivate.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CustomerResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<CustomerResponseDto>.Error(
                "INVALID_CUSTOMER_ID",
                ["Route id and payload id must match."]));
        }

        var response = await _mediator.Send(command with { Id = id }, cancellationToken);
        return this.ToResult(response);
    }

    /// <summary>
    /// Activates the customer, or confirms it when it is already active. Same permission as <c>PUT</c>.
    /// </summary>
    [HttpPost("{id:guid}/activate")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<CustomerResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(
        Guid id,
        [FromBody] CustomerStatusChangeRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ActivateCustomerCommand(id, body?.Notes), cancellationToken);
        return this.ToResult(response);
    }

    /// <summary>
    /// Deactivates the customer; 200 without changes when it is already inactive. Same permission as <c>PUT</c>.
    /// </summary>
    [HttpPost("{id:guid}/deactivate")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<CustomerResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(
        Guid id,
        [FromBody] CustomerStatusChangeRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DeactivateCustomerCommand(id, body?.Notes), cancellationToken);
        return this.ToResult(response);
    }

    /// <summary>
    /// The customer's status events, newest first.
    /// </summary>
    [HttpGet("{id:guid}/status-history")]
    [ProducesResponseType(typeof(Response<PagedResult<CustomerStatusHistoryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatusHistory(
        Guid id,
        [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCustomerStatusHistoryQuery(id, pageNumber, pageSize), cancellationToken);
        return this.ToResult(response);
    }
}
