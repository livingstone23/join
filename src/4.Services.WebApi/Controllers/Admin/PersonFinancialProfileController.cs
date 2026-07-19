using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using DeletePersonFinancialProfileCommand = JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands.DeletePersonFinancialProfileCommand;
using UpdatePersonFinancialProfileCommand = JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands.UpdatePersonFinancialProfileCommand;



namespace JOIN.Services.WebApi.Controllers.Admin;



/// <summary>
/// Exposes endpoints for managing person financial profiles in a tenant-safe way.
/// The controller remains thin and delegates business rules to MediatR handlers.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("Persons")]
public class PersonFinancialProfileController(IMediator mediator) : ControllerBase
{
    private readonly IMediator _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));

    /// <summary>
    /// Retrieves a single person financial profile by its unique identifier.
    /// </summary>
    /// <param name="id">The person financial profile identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response with the financial profile detail when found.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<PersonFinancialProfileResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetPersonFinancialProfileByIdQuery(id), cancellationToken);

        if (!response.IsSuccess)
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Creates a new financial profile for the specified person.
    /// </summary>
    /// <param name="command">The payload with financial profile data.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 201 response with the newly created financial profile identifier.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] CreatePersonFinancialProfileCommand command, CancellationToken cancellationToken = default)
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
    /// Updates an existing person financial profile.
    /// </summary>
    /// <param name="id">The financial profile identifier from the route.</param>
    /// <param name="command">The payload with the updated financial profile data.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response when the update succeeds.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePersonFinancialProfileCommand command, CancellationToken cancellationToken = default)
    {
        if (command.Id != Guid.Empty && command.Id != id)
        {
            return BadRequest(Response<Guid>.Error(
                "INVALID_FINANCIAL_PROFILE_ID",
                ["Route id and payload id must match."]));
        }

        var request = command with { Id = id };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "PERSON_FINANCIAL_PROFILE_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Lists all financial profiles for a specific person in the current tenant.
    /// </summary>
    /// <param name="personId">The person identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response with the person financial profile list.</returns>
    [HttpGet("person/{personId:guid}")]
    [ProducesResponseType(typeof(Response<List<PersonFinancialProfileResponseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetByPersonId(Guid personId, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetPersonFinancialProfilesByPersonIdQuery(personId), cancellationToken);

        if (!response.IsSuccess)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Performs a soft delete on a person financial profile by marking its logical delete flag.
    /// </summary>
    /// <param name="id">The person financial profile identifier.</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A 200 response when the soft delete succeeds.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new DeletePersonFinancialProfileCommand(id), cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.Message == "PERSON_FINANCIAL_PROFILE_NOT_FOUND")
            {
                return NotFound(response);
            }

            return BadRequest(response);
        }

        return Ok(response);
    }
}
