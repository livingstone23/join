using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.CustomerAddresses.Commands;
using JOIN.Application.UseCases.Admin.CustomerAddresses.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using DeleteCustomerAddressCommand = JOIN.Application.UseCases.Admin.CustomerAddresses.Commands.DeleteCustomerAddressCommand;
using UpdateCustomerAddressCommand = JOIN.Application.UseCases.Admin.CustomerAddresses.Commands.UpdateCustomerAddressCommand;



namespace JOIN.Services.WebApi.Controllers.Admin;



/// <summary>
/// Exposes endpoints for managing customer addresses in a tenant-safe way.
/// The controller remains thin and delegates business rules to MediatR handlers.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("Customers")]
public class CustomerAddressController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Retrieves a single customer address by its unique identifier.
    /// </summary>
    /// <param name="id">The customer address identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response with the address detail when found.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CustomerAddressResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCustomerAddressByIdQuery(id), cancellationToken);

        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new address for the specified customer.
    /// </summary>
    /// <param name="command">The payload with address data.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 201 response with the newly created address identifier.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreateCustomerAddressCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "CUSTOMER_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetByCustomerId), new { customerId = command.CustomerId }, response);
    }

    /// <summary>
    /// Updates an existing customer address.
    /// </summary>
    /// <param name="id">The address identifier from the route.</param>
    /// <param name="command">The payload with the updated address data.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response when the update succeeds.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCustomerAddressCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<Guid>.Error(
                "INVALID_ADDRESS_ID",
                ["Route id and payload id must match."]));
        }

        var request = command with { Id = id };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "CUSTOMER_ADDRESS_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Lists all addresses for a specific customer in the current tenant.
    /// </summary>
    /// <param name="customerId">The customer identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response with the customer address list.</returns>
    [HttpGet("customer/{customerId:guid}")]
    [ProducesResponseType(typeof(Response<List<CustomerAddressResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetByCustomerId(Guid customerId, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetCustomerAddressesByCustomerIdQuery(customerId), cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete on a customer address by marking its logical delete flag.
    /// </summary>
    /// <param name="id">The customer address identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response when the soft delete succeeds.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DeleteCustomerAddressCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "CUSTOMER_ADDRESS_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
