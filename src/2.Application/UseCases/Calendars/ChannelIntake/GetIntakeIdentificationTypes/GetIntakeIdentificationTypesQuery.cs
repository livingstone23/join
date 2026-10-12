using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.GetIntakeIdentificationTypes;

/// <summary>
/// Active global identification types the agent may offer when registering a new person (SPEC 45).
/// </summary>
public sealed record GetIntakeIdentificationTypesQuery
    : IRequest<Response<IReadOnlyList<IntakeIdentificationTypeDto>>>;