using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Queries.GetCalendarActivityCompanyById;

/// <summary>
/// One activity type of the company.
/// </summary>
public record GetCalendarActivityCompanyByIdQuery(Guid Id) : IRequest<Response<CalendarActivityCompanyDto>>;
