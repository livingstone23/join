
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.Customers.Commands;
using JOIN.Application.UseCases.Admin.Customers.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers;

/// <summary>
/// Exposes REST endpoints for managing customer aggregates.
/// The controller keeps a thin transport-oriented role and delegates all business rules, validation, and persistence orchestration to MediatR handlers.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class CustomersController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Retrieves a single customer aggregate by its unique identifier.
    /// This endpoint is used when a client needs the full customer payload, including the data required to display or edit the aggregate safely.
    /// </summary>
    /// <param name="id">The unique identifier of the customer to retrieve.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>A standardized response containing the requested customer data when the aggregate exists.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCustomerByIdQuery(id), cancellationToken);

        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieves a paginated customer list for the active tenant with optional filtering by person type, names, commercial name, and identification fields.
    /// This endpoint is intended for administrative listing screens that need server-side paging and search behavior.
    /// </summary>
    /// <param name="pageNumber">The 1-based page number to retrieve. The default value is 1.</param>
    /// <param name="pageSize">The maximum number of customer rows to return for the requested page.</param>
    /// <param name="personType">Optional filter used to restrict the result set by customer person type.</param>
    /// <param name="firstName">Optional filter applied to the first-name portion of the customer record.</param>
    /// <param name="middleName">Optional filter applied to the middle-name portion of the customer record.</param>
    /// <param name="lastName">Optional filter applied to the first last-name portion of the customer record.</param>
    /// <param name="secondLastName">Optional filter applied to the second last-name portion of the customer record.</param>
    /// <param name="commercialName">Optional filter applied to the commercial or trade name field.</param>
    /// <param name="identificationTypeId">Optional identification-type filter used to narrow the query.</param>
    /// <param name="identificationNumber">Optional identification-number filter used to find specific customer records.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the paged query executes.</param>
    /// <returns>A standardized paged response containing the matching customers.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CustomerListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? personType = null,
        [FromQuery] string? firstName = null,
        [FromQuery] string? middleName = null,
        [FromQuery] string? lastName = null,
        [FromQuery] string? secondLastName = null,
        [FromQuery] string? commercialName = null,
        [FromQuery] Guid? identificationTypeId = null,
        [FromQuery] string? identificationNumber = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetCustomersPagedQuery(
            pageNumber,
            pageSize,
            personType,
            firstName,
            middleName,
            lastName,
            secondLastName,
            commercialName,
            identificationTypeId,
            identificationNumber);

        var response = await _mediator.Send(query, cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new customer aggregate in the current tenant context.
    /// The command persists the main customer record and may also initialize related value objects and child collections according to the business rules enforced in the Application layer.
    /// </summary>
    /// <param name="command">The payload containing the customer information to create.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the create command is executing.</param>
    /// <returns>A `201 Created` response containing the identifier of the new customer aggregate.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "CUSTOMER_ALREADY_EXISTS")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.Data }, response);
    }

    /// <summary>
    /// Updates an existing customer aggregate and synchronizes its related data using the route identifier as the authoritative resource key.
    /// The endpoint returns validation, not-found, or conflict responses when the underlying command cannot be completed successfully.
    /// </summary>
    /// <param name="id">The unique identifier of the customer to update.</param>
    /// <param name="command">The full update payload containing the desired customer state.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the update command is running.</param>
    /// <returns>A standardized response containing the updated customer identifier.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<Guid>.Error("INVALID_CUSTOMER_ID", ["Route id and payload id must match."]));
        }

        var request = command with { Id = id };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "CUSTOMER_NOT_FOUND")
            {
                return NotFound(response);
            }

            if (response.Message == "CUSTOMER_IDENTIFICATION_IN_USE")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete over a customer aggregate while preserving the standardized response contract.
    /// The underlying command determines whether the customer exists and whether the deletion request is valid.
    /// </summary>
    /// <param name="id">The unique identifier of the customer to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the delete command is processed.</param>
    /// <returns>A standardized response containing the deleted customer identifier.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DeleteCustomerCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "CUSTOMER_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
