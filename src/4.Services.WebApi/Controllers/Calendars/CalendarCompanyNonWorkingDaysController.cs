using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.CreateCalendarCompanyNonWorkingDay;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.DeleteCalendarCompanyNonWorkingDay;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.UpdateCalendarCompanyNonWorkingDay;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDayById;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDays;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDaysRange;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Calendars;

/// <summary>
/// Company holidays (SPEC 44). Resource <c>CalendarSettings</c>; <c>range</c> uses <c>Calendar</c> so the views can
/// mark the holidays.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("CalendarSettings")]
public class CalendarCompanyNonWorkingDaysController(ISender sender) : ControllerBase
{
    /// <summary> Paged holidays of a year (recurring ones resolved for that year). </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<CalendarCompanyNonWorkingDayDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int? year = null, [FromQuery] int? page = null, [FromQuery] int? pageNumber = null,
        [FromQuery] int? pageSize = null, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarCompanyNonWorkingDaysQuery(year, page ?? pageNumber, pageSize), cancellationToken));

    /// <summary> Holidays of a date range, both ends inclusive. </summary>
    [HttpGet("range")]
    [PermissionResource("Calendar")]
    [ProducesResponseType(typeof(Response<IReadOnlyList<CalendarCompanyNonWorkingDayDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRange([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarCompanyNonWorkingDaysRangeQuery(from, to), cancellationToken));

    /// <summary> Detail. </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarCompanyNonWorkingDayDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new GetCalendarCompanyNonWorkingDayByIdQuery(id), cancellationToken));

    /// <summary> Creates a holiday. </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Response<CalendarCompanyNonWorkingDayDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateCalendarCompanyNonWorkingDayCommand command, CancellationToken cancellationToken = default) =>
        this.ToCreated(await sender.Send(command, cancellationToken));

    /// <summary> Updates a holiday. </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Response<CalendarCompanyNonWorkingDayDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateCalendarCompanyNonWorkingDayCommand command, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(command with { Id = id }, cancellationToken));

    /// <summary> Deletes a holiday. </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToResult(await sender.Send(new DeleteCalendarCompanyNonWorkingDayCommand(id), cancellationToken));
}
