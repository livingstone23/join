using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Queries.GetCalendarStatusCompanyById;

/// <summary>
/// One status of the company.
/// </summary>
public record GetCalendarStatusCompanyByIdQuery(Guid Id) : IRequest<Response<CalendarStatusCompanyDto>>;
