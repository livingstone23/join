using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.CancelIntakeEvent;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.CreateIntakeEvent;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RegisterIntakePerson;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RescheduleIntakeEvent;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.GetIntakeAvailability;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.GetIntakeCompanies;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.GetIntakeIdentificationTypes;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.GetIntakePersonEvents;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.LookupIntakePerson;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.LookupIntakeUserByEmail;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Endpoints of the channel agent (n8n over WhatsApp, e-mail…) authenticated as a service user with the
/// <c>Agent</c> role (SPEC 45). The company is the one of the token: the agent lists its companies with
/// <c>GET /companies</c> and switches with <c>POST /workspaces/switch-company</c>. Resource
/// <c>CalendarChannelIntake</c>, module <c>Calendar</c> (SPEC 48).
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarChannelIntake")]
public class CalendarChannelIntakeController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Companies of the agent with the calendar operational. Does not depend on the company of the token, so it skips
    /// the resource check; the handler requires the <c>Agent</c> or <c>SuperAdmin</c> role (403 otherwise).
    /// </summary>
    [Authorize]
    [SkipDynamicAuthorization]
    [HttpGet("companies")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<IntakeCompanyDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCompanies([FromQuery] string? search = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetIntakeCompaniesQuery(search), cancellationToken));

    /// <summary> Active identification types, to ask the person for their data. </summary>
    [HttpGet("identification-types")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<IntakeIdentificationTypeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIdentificationTypes(CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetIntakeIdentificationTypesQuery(), cancellationToken));

    /// <summary> The person who writes, by channel identifier or by identification. Only id and name. </summary>
    [HttpGet("persons/lookup")]
    [ProducesResponseType(typeof(Response<IntakePersonLookupDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> LookupPerson(
        [FromQuery] string? channelCode = null,
        [FromQuery] string? identifier = null,
        [FromQuery] Guid? identificationTypeId = null,
        [FromQuery] string? identificationNumber = null,
        CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new LookupIntakePersonQuery(channelCode, identifier, identificationTypeId, identificationNumber), cancellationToken));

    /// <summary>
    /// Registers a new person (201). A person that already exists by identification is returned with 200 and
    /// <c>created = false</c>.
    /// </summary>
    [HttpPost("persons")]
    [ProducesResponseType(typeof(Response<RegisterIntakePersonDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<RegisterIntakePersonDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterPerson([FromBody] RegisterIntakePersonCommand command, CancellationToken cancellationToken = default)
    {
        var response = await sender.Send(command, cancellationToken);
        return response is { IsSuccess: true, Data.Created: false } ? this.ToResult(response) : this.ToCreated(response);
    }

    /// <summary> Exact e-mail of the user who attends the requester. No listing, no search by name. </summary>
    [HttpGet("users/lookup")]
    [ProducesResponseType(typeof(Response<IntakeUserLookupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupUser([FromQuery] string? email, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new LookupIntakeUserByEmailQuery(email ?? string.Empty), cancellationToken));

    /// <summary> Free slots of a user (default user without <c>userId</c>) for an activity type, at most 14 days. </summary>
    [HttpGet("availability")]
    [ProducesResponseType(typeof(Response<IntakeAvailabilityDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAvailability(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] string activityCode,
        [FromQuery] Guid? userId = null,
        CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetIntakeAvailabilityQuery(userId, from, to, activityCode), cancellationToken));

    /// <summary>
    /// Creates the requested activity (201). A retry with the same <c>originReference</c> returns the existing
    /// activity with 200 and <c>isDuplicate = true</c>.
    /// </summary>
    [HttpPost("events")]
    [ProducesResponseType(typeof(Response<CreateIntakeEventDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<CreateIntakeEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateEvent([FromBody] CreateIntakeEventCommand command, CancellationToken cancellationToken = default)
    {
        var response = await sender.Send(command, cancellationToken);
        return response is { IsSuccess: true, Data.IsDuplicate: true } ? this.ToResult(response) : this.ToCreated(response);
    }

    /// <summary> Non-cancelled activities of the person from today (at most 90 days). </summary>
    [HttpGet("events")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<IntakeEventDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPersonEvents(
        [FromQuery] Guid requesterPersonId,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetIntakePersonEventsQuery(requesterPersonId, from, to), cancellationToken));

    /// <summary> Reschedules an activity of the requester. </summary>
    [HttpPut("events/{id:guid}/reschedule")]
    [ProducesResponseType(typeof(Response<RescheduleIntakeEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RescheduleEvent(Guid id, [FromBody] RescheduleIntakeEventCommand command, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { EventId = id }, cancellationToken));

    /// <summary> Cancels an activity of the requester (an update of its status). </summary>
    [HttpPost("events/{id:guid}/cancel")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<CancelIntakeEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelEvent(Guid id, [FromBody] CancelIntakeEventCommand command, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { EventId = id }, cancellationToken));
}
