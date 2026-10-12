using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.GetIntakeAvailability;

/// <summary>
/// Free slots of a user (or the company default user) for an activity code in a range, at most 14 days
/// (SPEC 45, <c>GET /availability</c>). Wraps the SPEC 44 panel availability calculator to reuse R4 and R5.
/// </summary>
public sealed record GetIntakeAvailabilityQuery(
    Guid? UserId,
    DateTimeOffset From,
    DateTimeOffset To,
    string ActivityCode)
    : IRequest<Response<IntakeAvailabilityDto>>;