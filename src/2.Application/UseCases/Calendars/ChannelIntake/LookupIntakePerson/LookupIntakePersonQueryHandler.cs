using JOIN.Application.Common;
using Dapper;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.LookupIntakePerson;

/// <summary>
/// Resolves a person through <see cref="IntakeRequesterResolver"/> for channel identifiers, or via an exact match
/// on <c>(IdentificationTypeId, IdentificationNumber)</c> for the identification pair (SPEC 45). When neither pair
/// is set, the query answers <c>400 CALENDAR_INTAKE_INVALID_CHANNEL</c>.
/// </summary>
public sealed class LookupIntakePersonQueryHandler(
    IntakeRequesterResolver resolver,
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<LookupIntakePersonQuery, Response<IntakePersonLookupDto>>
{
    public async Task<Response<IntakePersonLookupDto>> Handle(LookupIntakePersonQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<IntakePersonLookupDto>.Error(CalendarErrorCodes.CompanyRequired,
                ["The X-Company-Id header or the CompanyId claim is required."]);
        }

        var hasChannel = !string.IsNullOrWhiteSpace(request.ChannelCode) && !string.IsNullOrWhiteSpace(request.Identifier);
        var hasIdentification = request.IdentificationTypeId.HasValue && request.IdentificationTypeId != Guid.Empty
                                && !string.IsNullOrWhiteSpace(request.IdentificationNumber);

        if (!hasChannel && !hasIdentification)
        {
            return Response<IntakePersonLookupDto>.Error(CalendarErrorCodes.IntakeInvalidChannel,
                ["Either channelCode+identifier or identificationTypeId+identificationNumber must be provided."]);
        }

        if (hasChannel)
        {
            var channelCode = request.ChannelCode!.Trim().ToUpperInvariant();
            var lookup = channelCode switch
            {
                CommunicationChannelCodes.WhatsApp or CommunicationChannelCodes.SendGrid or CommunicationChannelCodes.Telegram
                    or CommunicationChannelCodes.Twilio or CommunicationChannelCodes.App
                    => await ResolveByChannelAsync(companyId, channelCode, request.Identifier!, cancellationToken),
                _ => LookupPersonResult.NotFound()
            };

            return Map(lookup);
        }

        // Identification path: exact match on (CompanyId, IdentificationTypeId, IdentificationNumber), active rows.
        const string sql = """
            SELECT id,
                   firstname || ' ' || COALESCE(lastname, '') AS fullname,
                   EXISTS(
                       SELECT 1 FROM admin.personpendingconfirmations ppc
                       WHERE ppc.companyid = @CompanyId
                         AND ppc.personid = p.id
                         AND ppc.status = 1
                         AND ppc.gcrecord = 0
                   ) AS haspendingconfirmation
              FROM admin.persons
             WHERE companyid = @CompanyId
               AND identificationtypeid = @IdentificationTypeId
               AND identificationnumber = @IdentificationNumber
               AND gcrecord = 0
               AND isactive = TRUE
             LIMIT 1;
            """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<IdentificationRow>(
            new CommandDefinition(sql, new
            {
                CompanyId = companyId,
                IdentificationTypeId = request.IdentificationTypeId!.Value,
                IdentificationNumber = request.IdentificationNumber!.Trim()
            }, cancellationToken: cancellationToken));

        return row is null
            ? new Response<IntakePersonLookupDto>
            {
                IsSuccess = true,
                Data = new IntakePersonLookupDto(false, null, null, false),
                Message = "No person found for the given identification."
            }
            : new Response<IntakePersonLookupDto>
            {
                IsSuccess = true,
                Data = new IntakePersonLookupDto(true, row.Id, row.FullName, row.HasPendingConfirmation),
                Message = "Person found."
            };
    }

    private async Task<LookupPersonResult> ResolveByChannelAsync(Guid companyId, string channelCode, string identifier, CancellationToken ct)
    {
        return channelCode switch
        {
            CommunicationChannelCodes.WhatsApp or CommunicationChannelCodes.Twilio or CommunicationChannelCodes.Telegram
                => await resolver.LookupByPhoneAsync(companyId, identifier, ct),
            CommunicationChannelCodes.SendGrid or CommunicationChannelCodes.App
                => await resolver.LookupByEmailAsync(companyId, identifier, ct),
            _ => LookupPersonResult.NotFound()
        };
    }

    private static Response<IntakePersonLookupDto> Map(LookupPersonResult lookup) =>
        new()
        {
            IsSuccess = true,
            Data = new IntakePersonLookupDto(lookup.Found, lookup.PersonId, lookup.FullName, lookup.HasPendingConfirmation, lookup.Ambiguous),
            Message = lookup.Ambiguous
                ? "Multiple persons match the identifier; ask for identification."
                : lookup.Found ? "Person found." : "No person found."
        };

    private sealed class IdentificationRow
    {
        public Guid Id { get; init; }
        public string FullName { get; init; } = string.Empty;
        public bool HasPendingConfirmation { get; init; }
    }
}