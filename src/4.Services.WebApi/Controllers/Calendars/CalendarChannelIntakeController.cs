using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using JOIN.Application.UseCases.Calendars.ChannelIntake;
using JOIN.Application.UseCases.Calendars.ChannelIntake.GetIntakeAvailability;
using JOIN.Application.UseCases.Calendars.ChannelIntake.GetIntakeCompanies;
using JOIN.Application.UseCases.Calendars.ChannelIntake.GetIntakeIdentificationTypes;
using JOIN.Application.UseCases.Calendars.ChannelIntake.GetIntakePersonEvents;
using JOIN.Application.UseCases.Calendars.ChannelIntake.LookupIntakePerson;
using JOIN.Application.UseCases.Calendars.ChannelIntake.LookupIntakeUserByEmail;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Endpoints for a channel agent (n8n over WhatsApp / e-mail / SMS) to identify the company, look up
/// a person, register a new one, and create / reschedule / cancel calendar activities on their behalf
/// (SPEC 45 F6). Resource <c>CalendarChannelIntake</c>; the <c>GET /companies</c> action is the only
/// one that bypasses the dynamic authorization filter because the company context is not known yet
/// and the handler enforces an <c>Agent</c> / <c>SuperAdmin</c> role check.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarChannelIntake")]
public class CalendarChannelIntakeController(ISender sender, ICurrentUserService currentUserService) : ControllerBase
{
    /// <summary> Companies the agent can act in. Bypasses the controller filter; the handler enforces Agent / SuperAdmin. </summary>
    [Authorize]
    [SkipDynamicAuthorization]
    [HttpGet("companies")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<IntakeCompanyDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCompanies(
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (!currentUserService.IsInRole("Agent") && !currentUserService.IsInRole("SuperAdmin"))
        {
            return StatusCode(StatusCodes.Status403Forbidden, Response<IReadOnlyList<IntakeCompanyDto>>.Error(
                "CALENDAR_INTAKE_AGENT_ROLE_REQUIRED",
                ["Only Agent or SuperAdmin users can list the intake companies."]));
        }

        var response = await sender.Send(new GetIntakeCompaniesQuery(search), cancellationToken);
        return this.ToResult(response);
    }

    /// <summary> Active identification types. </summary>
    [HttpGet("identification-types")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<IntakeIdentificationTypeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIdentificationTypes(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        CancellationToken cancellationToken = default)
    {
        var response = await sender.Send(new GetIntakeIdentificationTypesQuery(), cancellationToken);
        return this.ToResult(response);
    }

    /// <summary> Look up a person by channel+identifier or by identification. </summary>
    [HttpGet("persons/lookup")]
    [ProducesResponseType(typeof(Response<IntakePersonLookupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupPerson(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromQuery] string? channelCode = null,
        [FromQuery] string? identifier = null,
        [FromQuery] Guid? identificationTypeId = null,
        [FromQuery] string? identificationNumber = null,
        CancellationToken cancellationToken = default)
    {
        var response = await sender.Send(
            new LookupIntakePersonQuery(channelCode, identifier, identificationTypeId, identificationNumber),
            cancellationToken);
        return this.ToResult(response);
    }

    /// <summary> Register a new person + primary contact + review activity (F4). </summary>
    [HttpPost("persons")]
    [ProducesResponseType(typeof(Response<RegisterIntakePersonDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterPerson(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] RegisterIntakePersonCommand body,
        CancellationToken cancellationToken = default)
    {
        var command = body with { CompanyId = companyId };
        var response = await sender.Send(command, cancellationToken);
        return this.ToCreated(response);
    }

    /// <summary> Exact email user lookup; no list, no name search. </summary>
    [HttpGet("users/lookup")]
    [ProducesResponseType(typeof(Response<IntakeUserLookupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupUser(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromQuery] string email,
        CancellationToken cancellationToken = default)
    {
        var response = await sender.Send(new LookupIntakeUserByEmailQuery(email), cancellationToken);
        return this.ToResult(response);
    }

    /// <summary> Free slots of a user for an activity type, at most 14 days. </summary>
    [HttpGet("availability")]
    [ProducesResponseType(typeof(Response<IntakeAvailabilityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailability(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromQuery] Guid? userId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] string activityCode,
        CancellationToken cancellationToken = default)
    {
        var response = await sender.Send(new GetIntakeAvailabilityQuery(userId, from, to, activityCode), cancellationToken);
        return this.ToResult(response);
    }

    /// <summary> Create the requested activity. </summary>
    [HttpPost("events")]
    [ProducesResponseType(typeof(Response<CreateIntakeEventDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateEvent(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] CreateIntakeEventCommand body,
        CancellationToken cancellationToken = default)
    {
        var command = body with { CompanyId = companyId };
        var response = await sender.Send(command, cancellationToken);
        return this.ToCreated(response);
    }

    /// <summary> Open activities of a person (not cancelled), up to 90 days. </summary>
    [HttpGet("events")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<IntakeEventDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPersonEvents(
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromQuery] Guid requesterPersonId,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var response = await sender.Send(new GetIntakePersonEventsQuery(requesterPersonId, from, to), cancellationToken);
        return this.ToResult(response);
    }

    /// <summary> Reschedule an activity owned by the requester. </summary>
    [HttpPut("events/{id:guid}/reschedule")]
    [ProducesResponseType(typeof(Response<CreateIntakeEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RescheduleEvent(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] RescheduleIntakeEventCommand body,
        CancellationToken cancellationToken = default)
    {
        var command = body with { CompanyId = companyId, EventId = id };
        var response = await sender.Send(command, cancellationToken);
        return this.ToResult(response);
    }

    /// <summary> Cancel an activity owned by the requester. </summary>
    [HttpPost("events/{id:guid}/cancel")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<CreateIntakeEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelEvent(
        Guid id,
        [FromHeader(Name = "X-Company-Id")] Guid companyId,
        [FromBody] CancelIntakeEventCommand body,
        CancellationToken cancellationToken = default)
    {
        var command = body with { CompanyId = companyId, EventId = id };
        var response = await sender.Send(command, cancellationToken);
        return this.ToResult(response);
    }
}
