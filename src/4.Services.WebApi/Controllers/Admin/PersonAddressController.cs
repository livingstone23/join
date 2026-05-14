using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.PersonAddresses.Commands;
using JOIN.Application.UseCases.Admin.PersonAddresses.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using DeletePersonAddressCommand = JOIN.Application.UseCases.Admin.PersonAddresses.Commands.DeletePersonAddressCommand;
using UpdatePersonAddressCommand = JOIN.Application.UseCases.Admin.PersonAddresses.Commands.UpdatePersonAddressCommand;



namespace JOIN.Services.WebApi.Controllers.Admin;



/// <summary>
/// Exposes endpoints for managing person addresses in a tenant-safe way.
/// The controller remains thin and delegates business rules to MediatR handlers.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[PermissionResource("Persons")]
public class PersonAddressController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Retrieves a single person  address by its unique identifier.
    /// </summary>
    /// <param name="id">The person address identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response with the address detail when found.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<PersonAddressResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetPersonAddressByIdQuery(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "PERSON_ADDRESS_NOT_FOUND")
            {
                return NotFound(response);
            }

            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new address for the specified person.
    /// </summary>
    /// <param name="command">The payload with address data.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 201 response with the newly created address identifier.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreatePersonAddressCommand command, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "PERSON_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return CreatedAtAction(nameof(GetByPersonId), new { personId = command.PersonId }, response);
    }

    /// <summary>
    /// Updates an existing person address.
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
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePersonAddressCommand command, CancellationToken cancellationToken = default)
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
            if (response.Message == "PERSON_ADDRESS_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Lists all addresses for a specific person in the current tenant.
    /// </summary>
    /// <param name="personId">The person identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response with the person address list.</returns>
    [HttpGet("person/{personId:guid}")]
    [ProducesResponseType(typeof(Response<List<PersonAddressResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetByPersonId(Guid personId, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetPersonAddressesByPersonIdQuery(personId), cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete on a person address by marking its logical delete flag.
    /// </summary>
    /// <param name="id">The person address identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response when the soft delete succeeds.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DeletePersonAddressCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "PERSON_ADDRESS_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
