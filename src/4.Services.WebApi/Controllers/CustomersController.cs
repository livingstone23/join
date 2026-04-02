


using JOIN.Application.DTO.Admin;
using JOIN.Application.Common;
using JOIN.Application.UseCases.Admin.Customers.Commands;
using JOIN.Application.UseCases.Admin.Customers.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;



namespace JOIN.Services.WebApi.Controllers;



/// <summary>
/// API Controller for managing Customer entities.
/// Acts strictly as a thin routing layer, delegating all business logic to MediatR handlers.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class CustomersController : ControllerBase
{   

    
    private readonly IMediator _mediator;

    public CustomersController(IMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }


    /// <summary>
    /// Retrieves a specific customer by their unique identifier.
    /// </summary>
    /// <param name="id">The GUID of the customer.</param>
    /// <returns>A standardized response containing the customer DTO.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Application.Common.Response<CustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var query = new GetCustomerByIdQuery(id);
        var response = await _mediator.Send(query);

        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }


    /// <summary>
    /// Retrieves a paginated list of customers for the current tenant.
    /// </summary>
    /// <param name="pageNumber">The requested page number. Defaults to 1.</param>
    /// <param name="pageSize">The requested page size. Maximum allowed value is 50.</param>
    /// <returns>A standardized response containing the paginated customer list.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(Application.Common.Response<PagedResult<CustomerListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPaged([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        var query = new GetCustomersPagedQuery(pageNumber, pageSize);
        var response = await _mediator.Send(query);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }
    

    /// <summary>
    /// Registers a new customer in the system.
    /// </summary>
    /// <param name="command">The command containing the customer data payload.</param>
    /// <returns>A standardized response containing the newly created customer ID.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Application.Common.Response<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Application.Common.Response<Guid>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCustomerCommand command)
    {
        var response = await _mediator.Send(command);

        if (!response.IsSuccess)
        {
            if (response.Message == "CUSTOMER_ALREADY_EXISTS")
            {
                return Conflict(response);
            }

            return BadRequest(response);
        }

        // Returns a 201 Created with the location of the new resource
        return CreatedAtAction(nameof(GetById), new { id = response.Data }, response);
    }


    /// <summary>
    /// Updates an existing customer and synchronizes its addresses and contacts.
    /// </summary>
    /// <param name="id">The GUID of the customer to update.</param>
    /// <param name="command">The command containing the full update payload.</param>
    /// <returns>A standardized response containing the updated customer ID.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Application.Common.Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Application.Common.Response<Guid>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCustomerCommand command)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<Guid>.Error("INVALID_CUSTOMER_ID", ["Route id and payload id must match."]));
        }

        var request = command with { Id = id };
        var response = await _mediator.Send(request);

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
    /// Performs a soft delete for a customer aggregate.
    /// </summary>
    /// <param name="id">The GUID of the customer to delete.</param>
    /// <returns>A standardized response containing the deleted customer ID.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Application.Common.Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var response = await _mediator.Send(new DeleteCustomerCommand(id));

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
