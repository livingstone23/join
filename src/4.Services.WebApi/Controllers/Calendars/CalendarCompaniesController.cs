using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.CreateCalendarCompany;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.ProvisionCalendarUsers;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.SyncCalendarCatalogs;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.UpdateCalendarCompany;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Queries.GetCurrentCalendarCompany;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Calendar configuration of the current company (SPEC 44). Resource <c>CalendarSettings</c>.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarSettings")]
public class CalendarCompaniesController(ISender sender) : ControllerBase
{
    /// <summary> Configuration of the current company; never 404 (<c>isConfigured = false</c>). </summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(Response<CalendarCompanyDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCurrentCalendarCompanyQuery(), cancellationToken));

    /// <summary> Creates the configuration and provisions the users' calendars. </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CalendarCompanyDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCalendarCompanyCommand command, CancellationToken cancellationToken = default) =>
        this.ToCreated(await sender.Send(command, cancellationToken));

    /// <summary> Updates the configuration. </summary>
    [HttpPut("current")]
    [ProducesResponseType(typeof(Response<CalendarCompanyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromBody] UpdateCalendarCompanyCommand command, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command, cancellationToken));

    /// <summary> Adds the global types and statuses the company does not have yet. </summary>
    [HttpPost("current/sync-catalogs")]
    [RequirePermission(PermissionFlags.CanExecute)]
    [ProducesResponseType(typeof(Response<CalendarCatalogSyncDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncCatalogs(CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new SyncCalendarCatalogsCommand(), cancellationToken));

    /// <summary> Creates the default calendar of the users still pending. </summary>
    [HttpPost("current/provision-users")]
    [RequirePermission(PermissionFlags.CanExecute)]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CalendarProvisionedUserDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ProvisionUsers(CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new ProvisionCalendarUsersCommand(), cancellationToken));
}
