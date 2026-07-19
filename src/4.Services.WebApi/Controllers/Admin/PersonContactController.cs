using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.PersonContacts.Commands;
using JOIN.Application.UseCases.Admin.PersonContacts.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using DeletePersonContactCommand = JOIN.Application.UseCases.Admin.PersonContacts.Commands.DeletePersonContactCommand;
using UpdatePersonContactCommand = JOIN.Application.UseCases.Admin.PersonContacts.Commands.UpdatePersonContactCommand;



namespace JOIN.Services.WebApi.Controllers.Admin;



/// <summary>
/// Exposes endpoints for managing person contacts in a tenant-safe way.
/// The controller remains thin and delegates business rules to MediatR handlers.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("Persons")]
public class PersonContactController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Retrieves a single person contact by its unique identifier.
    /// </summary>
    /// <param name="id">The person contact identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response with the contact detail when found.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<PersonContactResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetPersonContactByIdQuery(id), cancellationToken);

        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new contact for the specified person.
    /// </summary>
    /// <param name="command">The payload with contact data.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 201 response with the newly created contact identifier.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreatePersonContactCommand command, CancellationToken cancellationToken = default)
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

        return CreatedAtAction(nameof(GetByPersonId), new { personId = command.PersonId }, response);
    }

    /// <summary>
    /// Updates an existing person contact.
    /// </summary>
    /// <param name="id">The contact identifier from the route.</param>
    /// <param name="command">The payload with the updated contact data.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response when the update succeeds.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePersonContactCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<Guid>.Error(
                "INVALID_CONTACT_ID",
                ["Route id and payload id must match."]));
        }

        var request = command with { Id = id };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "CUSTOMER_CONTACT_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Lists all contacts for a specific person in the current tenant.
    /// </summary>
    /// <param name="personId">The person identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response with the person contact list.</returns>
    [HttpGet("person/{personId:guid}")]
    [ProducesResponseType(typeof(Response<List<PersonContactResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetByPersonId(Guid personId, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetPersonContactsByPersonIdQuery(personId), cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete on a person contact by marking its logical delete flag.
    /// </summary>
    /// <param name="id">The person contact identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response when the soft delete succeeds.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DeletePersonContactCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "CUSTOMER_CONTACT_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
