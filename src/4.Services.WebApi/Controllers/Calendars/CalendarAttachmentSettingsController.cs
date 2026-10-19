using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Commands;
using JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// SPEC 69 — per-company calendar attachment limits (regla 7). The endpoint lives under
/// <c>CalendarSettings</c> (the "Adjuntos" tab in the calendar configuration page), so the resource is the
/// same; the route is <c>/api/v1/CalendarAttachmentSettings</c>. Flags follow the HTTP verb: <c>POST</c> needs
/// <c>CalendarSettings.CanCreate</c> and <c>PUT</c> <c>CalendarSettings.CanUpdate</c>; the default seed creates the
/// row for every new company, so the screen normally only uses <c>GET</c> and <c>PUT</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarSettings")]
public class CalendarAttachmentSettingsController(ISender sender) : ControllerBase
{
    /// <summary> Returns the unique live row of the current tenant, or 400 when it does not exist yet. </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<CalendarAttachmentSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken) =>
        this.ToResult(await sender.Send(new GetCalendarAttachmentSettingsQuery(), cancellationToken));

    /// <summary> Creates the row (admin creates it once; subsequent calls fail with
    /// <c>CALENDAR_ATTACHMENT_SETTINGS_ALREADY_EXISTS</c>). </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CalendarAttachmentSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] CreateCalendarAttachmentSettingsCommand command, CancellationToken cancellationToken) =>
        this.ToResult(await sender.Send(command, cancellationToken));

    /// <summary> Replaces the row's limits. </summary>
    [HttpPut]
    [ProducesResponseType(typeof(Response<CalendarAttachmentSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update([FromBody] UpdateCalendarAttachmentSettingsCommand command, CancellationToken cancellationToken) =>
        this.ToResult(await sender.Send(command, cancellationToken));
}