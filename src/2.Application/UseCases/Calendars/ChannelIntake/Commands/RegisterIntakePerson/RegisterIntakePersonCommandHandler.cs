using System.Globalization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RegisterIntakePerson;

/// <summary>
/// Handles <see cref="RegisterIntakePersonCommand"/> (SPEC 45) in one transaction:
/// <list type="number">
///   <item>Calendar configuration of the company and active channel.</item>
///   <item>Active person with the same identification → returned without creating or changing anything (the new
///   phone is not added to it).</item>
///   <item>Phone or e-mail of another person → <c>CALENDAR_INTAKE_CONTACT_BELONGS_TO_OTHER_PERSON</c> (who it is is
///   not revealed).</item>
///   <item><c>Person</c> + <c>PersonContact</c> (<see cref="PersonIntakeRegistrar"/>), the <c>PERSON_REVIEW</c>
///   activity in the default user's calendar (<see cref="CalendarEventCommandProcessor"/>, schedule rules included)
///   and the <c>PersonPendingConfirmation</c> pointing at it.</item>
/// </list>
/// </summary>
public sealed class RegisterIntakePersonCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCompanyGuard companyGuard,
    CalendarCatalogReader catalogReader,
    IntakeChannelReader channelReader,
    IntakeRequesterResolver resolver,
    PersonIntakeRegistrar registrar,
    CalendarEventCommandProcessor processor)
    : IRequestHandler<RegisterIntakePersonCommand, Response<RegisterIntakePersonDto>>
{
    /// <summary> The review activity starts at the next multiple of this many minutes. </summary>
    public const int ReviewRoundingMinutes = 15;

    public async Task<Response<RegisterIntakePersonDto>> Handle(RegisterIntakePersonCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<RegisterIntakePersonDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var company = await companyGuard.GetContextAsync(companyId, cancellationToken);
        if (!company.IsSuccess)
        {
            return Response<RegisterIntakePersonDto>.Error(company.Message, company.Errors);
        }

        var channel = await channelReader.FindActiveAsync(request.OriginChannelCode);
        if (channel is null)
        {
            return Response<RegisterIntakePersonDto>.Error(CalendarErrorCodes.IntakeInvalidChannel,
                [$"The channel '{request.OriginChannelCode}' is not an active agent channel."]);
        }

        var existing = await resolver.LookupByIdentificationAsync(companyId, request.IdentificationTypeId, request.IdentificationNumber, cancellationToken);
        if (existing.Found)
        {
            return new Response<RegisterIntakePersonDto>
            {
                IsSuccess = true,
                Data = new RegisterIntakePersonDto(existing.PersonId!.Value, Created: false, HasPendingConfirmation: existing.HasPendingConfirmation),
                Message = "The person already exists; nothing was created or changed."
            };
        }

        if (await ContactBelongsToOtherPersonAsync(companyId, request, cancellationToken))
        {
            return Response<RegisterIntakePersonDto>.Error(CalendarErrorCodes.IntakeContactBelongsToOtherPerson,
                ["The phone or e-mail already belongs to another person of the company."]);
        }

        var review = await catalogReader.GetUsableActivityByCodeAsync(companyId, CalendarCodes.Activities.PersonReview);
        if (review is null)
        {
            return Response<RegisterIntakePersonDto>.Error(CalendarErrorCodes.ActivityNotEnabled,
                [$"The activity type '{CalendarCodes.Activities.PersonReview}' is not enabled in the company."]);
        }

        var defaultUserId = company.Data!.DefaultActivityUserId;
        if (!await HasCalendarAsync(companyId, defaultUserId))
        {
            return Response<RegisterIntakePersonDto>.Error(CalendarErrorCodes.IntakeTargetUserHasNoCalendar,
                ["The default activity user of the company has no calendar."]);
        }

        var person = await registrar.CreatePersonAsync(companyId, new PersonIntakeData(
            request.PersonType,
            request.FirstName,
            request.LastName,
            request.CommercialName,
            request.IdentificationTypeId,
            request.IdentificationNumber,
            request.GenderId,
            request.Phone,
            string.Equals(channel.Code, CommunicationChannelCodes.WhatsApp, StringComparison.OrdinalIgnoreCase) ? ContactType.WhatsApp : ContactType.MobilePhone,
            request.Email), cancellationToken);

        var now = DateTime.UtcNow;
        var fullName = string.IsNullOrWhiteSpace(person.CommercialName)
            ? $"{person.FirstName} {person.LastName}".Trim()
            : person.CommercialName;
        var input = new IntakeEventInput
        {
            CalendarActivityCompanyId = review.ActivityCompany.Id,
            Title = $"Revisar persona creada: {fullName}",
            Description = BuildReviewDescription(request, fullName, channel.Code, now),
            Start = new DateTimeOffset(RoundUp(now), TimeSpan.Zero),
            RequesterPersonId = person.Id
        };

        // The review activity does not take the message id: the agent may reuse it for the activity it creates next.
        var origin = new CalendarExternalOrigin(channel.Id, IntakeChannels.OriginTypeOf(channel.Code), request.OriginIdentifier.Trim(), null);
        var created = await processor.CreateAsync(input, defaultUserId, cancellationToken, origin);
        if (!created.IsSuccess)
        {
            return Response<RegisterIntakePersonDto>.Error(created.Message, created.Errors);
        }

        var pending = await registrar.AddPendingAsync(companyId, person.Id, request.Reason, channel.Id, request.OriginIdentifier, created.Data!.Id, cancellationToken);

        return new Response<RegisterIntakePersonDto>
        {
            IsSuccess = true,
            Data = new RegisterIntakePersonDto(person.Id, Created: true, pending.Id, created.Data.Id, HasPendingConfirmation: true),
            Message = "Person registered; a review activity was created for the default user."
        };
    }

    /// <summary> Next multiple of <see cref="ReviewRoundingMinutes"/> minutes (UTC), or the same instant when exact. </summary>
    public static DateTime RoundUp(DateTime utc)
    {
        var quarter = TimeSpan.FromMinutes(ReviewRoundingMinutes).Ticks;
        return new DateTime((utc.Ticks + quarter - 1) / quarter * quarter, DateTimeKind.Utc);
    }

    private async Task<bool> ContactBelongsToOtherPersonAsync(Guid companyId, RegisterIntakePersonCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Phone)
            && (await resolver.LookupByPhoneAsync(companyId, request.Phone, cancellationToken)).Exists)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(request.Email)
            && (await resolver.LookupByEmailAsync(companyId, request.Email, cancellationToken)).Exists;
    }

    private async Task<bool> HasCalendarAsync(Guid companyId, Guid userId) =>
        (await unitOfWork.GetRepository<CalendarUserConfiguration>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.UserId == userId && x.GcRecord == 0))
        .Any();

    private static string BuildReviewDescription(RegisterIntakePersonCommand request, string fullName, string? channelCode, DateTime nowUtc)
    {
        var lines = new List<string>
        {
            $"Persona: {fullName}",
            $"Identificación: {request.IdentificationNumber.Trim()}"
        };
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            lines.Add($"Teléfono: {request.Phone.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            lines.Add($"Correo: {request.Email.Trim()}");
        }

        lines.Add($"Motivo: {request.Reason.Trim()}");
        lines.Add($"Canal: {channelCode}");
        lines.Add($"Identificador: {request.OriginIdentifier.Trim()}");
        lines.Add($"Fecha: {nowUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)}");
        return string.Join('\n', lines);
    }
}
