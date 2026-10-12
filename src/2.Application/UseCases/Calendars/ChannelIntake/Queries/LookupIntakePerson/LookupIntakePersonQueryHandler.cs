using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.LookupIntakePerson;

/// <summary>
/// Handles <see cref="LookupIntakePersonQuery"/> with <see cref="IntakeRequesterResolver"/>: by the channel identifier
/// when <c>channelCode</c> + <c>identifier</c> are given, otherwise by the identification pair. Answers only the id,
/// the name and whether a pending confirmation is open (SPEC 45).
/// </summary>
public sealed class LookupIntakePersonQueryHandler(
    IntakeRequesterResolver resolver,
    ICurrentUserService currentUserService)
    : IRequestHandler<LookupIntakePersonQuery, Response<IntakePersonLookupDto>>
{
    public async Task<Response<IntakePersonLookupDto>> Handle(LookupIntakePersonQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<IntakePersonLookupDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var byChannel = !string.IsNullOrWhiteSpace(request.ChannelCode) && !string.IsNullOrWhiteSpace(request.Identifier);
        var byIdentification = request.IdentificationTypeId is { } typeId && typeId != Guid.Empty
                               && !string.IsNullOrWhiteSpace(request.IdentificationNumber);
        if (!byChannel && !byIdentification)
        {
            return Response<IntakePersonLookupDto>.Error(CalendarErrorCodes.IntakeLookupCriteriaRequired,
                ["Send channelCode + identifier, or identificationTypeId + identificationNumber."]);
        }

        if (byChannel && !IntakeChannels.IsSupported(request.ChannelCode))
        {
            return Response<IntakePersonLookupDto>.Error(CalendarErrorCodes.IntakeInvalidChannel,
                [$"The channel '{request.ChannelCode}' is not an agent channel."]);
        }

        var match = byChannel
            ? await resolver.LookupByChannelAsync(companyId, request.ChannelCode!, request.Identifier!, cancellationToken)
            : await resolver.LookupByIdentificationAsync(companyId, request.IdentificationTypeId!.Value, request.IdentificationNumber!, cancellationToken);

        return new Response<IntakePersonLookupDto>
        {
            IsSuccess = true,
            Data = new IntakePersonLookupDto(match.Found, match.PersonId, match.FullName, match.HasPendingConfirmation, match.Ambiguous),
            Message = match.Ambiguous
                ? "More than one person matches; ask for the identification."
                : match.Found ? "Person found." : "No person found."
        };
    }
}
