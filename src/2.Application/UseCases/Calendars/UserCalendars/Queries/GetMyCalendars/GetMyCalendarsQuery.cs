using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Queries.GetMyCalendars;

/// <summary>
/// The current user's calendars in the current company, with the default flag.
/// </summary>
public record GetMyCalendarsQuery : IRequest<Response<MyCalendarsDto>>;
