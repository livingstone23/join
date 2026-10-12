using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// Handles <see cref="RegisterIntakePersonCommand"/> (SPEC 45 F4). One transaction:
/// <list type="number">
///   <item>Resolves <see cref="CalendarCompany"/> and the channel.</item>
///   <item>Detects an existing person by identification and short-circuits with <c>Created = false</c>.</item>
///   <item>Detects a phone/e-mail collision with another person (<c>409</c>).</item>
///   <item>Creates <c>Person</c> + <c>PersonContact</c> + a <c>PERSON_REVIEW</c> activity in the default
///   user's calendar + a <c>PersonPendingConfirmation</c>.</item>
/// </list>
/// </summary>
public sealed class RegisterIntakePersonCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ISqlConnectionFactory connectionFactory,
    CalendarCompanyGuard companyGuard,
    CalendarEventScheduleGuard scheduleGuard,
    CalendarEventLogWriter logWriter,
    CalendarEventInitialStatusResolver statusResolver)
    : IRequestHandler<RegisterIntakePersonCommand, Response<RegisterIntakePersonDto>>
{
    private const string PersonReviewByCodeSql = """
        SELECT ac.Id, ac.DefaultDurationMinutes, ac.AllowsOverlap, ac.RequiresBusinessHours,
               ac.InitialCalendarStatusCompanyId
          FROM Calendar.ActivityCompanies ac
          INNER JOIN Calendar.Activities a ON a.Id = ac.CalendarActivityId
         WHERE ac.CompanyId = @TenantId
           AND ac.GcRecord = 0 AND ac.IsActive = TRUE
           AND a.GcRecord = 0 AND a.IsActive = TRUE
           AND a.Code = @Code
         LIMIT 1;
        """;

    private const string DuplicatePersonSql = """
        SELECT p.Id
          FROM admin.persons p
         WHERE p.companyid = @TenantId
           AND p.identificationtypeid = @IdentificationTypeId
           AND p.identificationnumber = @IdentificationNumber
           AND p.gcrecord = 0
           AND p.isactive = TRUE
         LIMIT 1;
        """;

    private const string HasPendingSql = """
        SELECT EXISTS(
            SELECT 1 FROM admin.personpendingconfirmations
             WHERE companyid = @TenantId
               AND personid = @PersonId
               AND status = 1
               AND gcrecord = 0
        );
        """;

    private const string PhoneCollisionSql = """
        SELECT pc.personid
          FROM admin.personcontacts pc
         WHERE pc.companyid = @TenantId
           AND pc.gcrecord = 0
           AND pc.isactive = TRUE
           AND pc.contacttype IN (3, 4, 5)
           AND regexp_replace(pc.contactvalue, '[^0-9]', '', 'g') = @Digits
         LIMIT 1;
        """;

    private const string EmailCollisionSql = """
        SELECT pc.personid
          FROM admin.personcontacts pc
         WHERE pc.companyid = @TenantId
           AND pc.gcrecord = 0
           AND pc.isactive = TRUE
           AND pc.contacttype IN (1, 2)
           AND LOWER(TRIM(pc.contactvalue)) = LOWER(TRIM(@Email))
         LIMIT 1;
        """;

    private const string UserConfigSql = """
        SELECT c.DefaultCalendarId
          FROM Calendar.UserConfigurations c
         WHERE c.CompanyId = @TenantId
           AND c.UserId = @UserId
           AND c.GcRecord = 0
         LIMIT 1;
        """;

    public async Task<Response<RegisterIntakePersonDto>> Handle(
        RegisterIntakePersonCommand request,
        CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId == Guid.Empty ? currentUserService.CompanyId : request.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<RegisterIntakePersonDto>.Error(CalendarErrorCodes.CompanyRequired,
                ["The X-Company-Id header or the CompanyId claim is required."]);
        }

        var companyResponse = await companyGuard.GetContextAsync(companyId, cancellationToken);
        if (!companyResponse.IsSuccess)
        {
            return Response<RegisterIntakePersonDto>.Error(companyResponse.Message, companyResponse.Errors);
        }
        var company = companyResponse.Data!;

        var channel = await FindChannelAsync(connectionFactory, request.OriginChannelCode, cancellationToken);
        if (channel is null)
        {
            return Response<RegisterIntakePersonDto>.Error(CalendarErrorCodes.IntakeInvalidChannel,
                [$"Channel '{request.OriginChannelCode}' is not registered or is inactive."]);
        }

        using var connection = connectionFactory.CreateConnection();

        // Duplicate by identification (active rows of the same company).
        var existingPersonId = await connection.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(DuplicatePersonSql,
                new { TenantId = companyId, request.IdentificationTypeId, request.IdentificationNumber },
                cancellationToken: cancellationToken));
        if (existingPersonId.HasValue && existingPersonId.Value != Guid.Empty)
        {
            var hasPending = await connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(HasPendingSql, new { TenantId = companyId, PersonId = existingPersonId.Value },
                    cancellationToken: cancellationToken));
            return new Response<RegisterIntakePersonDto>
            {
                IsSuccess = true,
                Data = new RegisterIntakePersonDto(existingPersonId.Value, false, HasPendingConfirmation: hasPending),
                Message = "Person already exists; no rows were created or modified."
            };
        }

        // Phone / e-mail collision (must NOT belong to another person of the same company).
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var digits = new string(request.Phone.Trim().Where(char.IsDigit).ToArray());
            var collisionOwner = await connection.QueryFirstOrDefaultAsync<Guid?>(
                new CommandDefinition(PhoneCollisionSql, new { TenantId = companyId, Digits = digits },
                    cancellationToken: cancellationToken));
            if (collisionOwner.HasValue && collisionOwner.Value != Guid.Empty)
            {
                return Response<RegisterIntakePersonDto>.Error(CalendarErrorCodes.IntakeContactBelongsToOtherPerson,
                    ["The phone belongs to another person in this company."]);
            }
        }
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var collisionOwner = await connection.QueryFirstOrDefaultAsync<Guid?>(
                new CommandDefinition(EmailCollisionSql, new { TenantId = companyId, Email = request.Email.Trim() },
                    cancellationToken: cancellationToken));
            if (collisionOwner.HasValue && collisionOwner.Value != Guid.Empty)
            {
                return Response<RegisterIntakePersonDto>.Error(CalendarErrorCodes.IntakeContactBelongsToOtherPerson,
                    ["The e-mail belongs to another person in this company."]);
            }
        }

        // PERSON_REVIEW activity type lookup.
        var activity = await connection.QueryFirstOrDefaultAsync<PersonReviewRow>(
            new CommandDefinition(PersonReviewByCodeSql, new { TenantId = companyId, Code = CalendarCodes.Activities.PersonReview },
                cancellationToken: cancellationToken));
        if (activity is null)
        {
            return Response<RegisterIntakePersonDto>.Error(CalendarErrorCodes.ActivityNotEnabled,
                [$"Activity '{CalendarCodes.Activities.PersonReview}' is not enabled in the company."]);
        }

        // Default user's calendar.
        var defaultCalendarId = await connection.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(UserConfigSql, new { TenantId = companyId, UserId = company.DefaultActivityUserId },
                cancellationToken: cancellationToken));
        if (defaultCalendarId is null)
        {
            return Response<RegisterIntakePersonDto>.Error(CalendarErrorCodes.IntakeTargetUserHasNoCalendar,
                ["The default user has no calendar configuration in this company."]);
        }

        // Initial status.
        var statusResponse = await statusResolver.ResolveAsync(BuildActivityCompany(activity));
        if (!statusResponse.IsSuccess)
        {
            return Response<RegisterIntakePersonDto>.Error(statusResponse.Message, statusResponse.Errors);
        }
        var status = statusResponse.Data!;
        var statusGlobal = await unitOfWork.GetRepository<Domain.Calendars.CalendarStatus>().GetAsync(status.CalendarStatusId);
        var blocksTime = statusGlobal?.BlocksTime ?? true;

        // Round start up to the next 15-minute boundary (UTC).
        var now = DateTime.UtcNow;
        var startUtc = RoundUpToNext15Minutes(now);
        var endUtc = startUtc.AddMinutes(activity.DefaultDurationMinutes);

        // Person.
        var person = new Person
        {
            CompanyId = companyId,
            PersonType = (PersonType)request.PersonType,
            FirstName = request.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? string.Empty : request.LastName.Trim(),
            CommercialName = string.IsNullOrWhiteSpace(request.CommercialName) ? null : request.CommercialName.Trim(),
            IdentificationTypeId = request.IdentificationTypeId,
            IdentificationNumber = request.IdentificationNumber.Trim(),
            GenderId = request.GenderId
        };
        await unitOfWork.GetRepository<Person>().InsertAsync(person);

        // Contacts.
        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            var phoneType = string.Equals(request.OriginChannelCode, CommunicationChannelCodes.WhatsApp,
                StringComparison.OrdinalIgnoreCase) ? ContactType.WhatsApp : ContactType.MobilePhone;
            var phoneContact = PersonContact.Create(companyId, person.Id, phoneType, request.Phone.Trim());
            phoneContact.SetAsPrimary();
            await unitOfWork.GetRepository<PersonContact>().InsertAsync(phoneContact);
        }
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var emailContact = PersonContact.Create(companyId, person.Id, ContactType.PrimaryEmail, request.Email.Trim());
            emailContact.SetAsPrimary();
            await unitOfWork.GetRepository<PersonContact>().InsertAsync(emailContact);
        }

        // Schedule guard (no-op for PERSON_REVIEW but called for symmetry).
        var candidates = new[] { new CalendarOccurrence(startUtc, endUtc) };
        var scheduleCheck = new CalendarScheduleCheck(
            company,
            company.DefaultActivityUserId,
            ActorIsOwner: false,
            activity.AllowsOverlap,
            activity.RequiresBusinessHours,
            blocksTime,
            IsAllDay: false,
            candidates,
            new HashSet<Guid>(),
            new HashSet<(Guid, DateTime)>());
        var validation = await scheduleGuard.ValidateAsync(scheduleCheck, cancellationToken);
        if (!validation.IsSuccess)
        {
            return Response<RegisterIntakePersonDto>.Error(validation.Message, validation.Errors);
        }

        // CalendarEvent.
        var originType = ResolveOriginType(request.OriginChannelCode, request.Phone, request.Email);
        var fullName = $"{person.FirstName} {person.LastName}".Trim();
        var description = BuildReviewDescription(person, request, channel.Code);
        var calendarEvent = new CalendarEvent
        {
            CompanyId = companyId,
            CalendarId = defaultCalendarId.Value,
            OwnerUserId = company.DefaultActivityUserId,
            CalendarActivityCompanyId = activity.Id,
            CalendarStatusCompanyId = status.Id,
            Title = $"Revisar persona creada: {fullName}",
            Description = description,
            IsAllDay = false,
            StartUtc = CalendarTime.AsUtc(startUtc),
            EndUtc = CalendarTime.AsUtc(endUtc),
            OriginChannelId = channel.Id,
            OriginType = originType,
            OriginIdentifier = request.OriginIdentifier,
            OriginReference = request.OriginReference,
            OriginUserId = ParseUserIdOrNull(currentUserService.UserId),
            RequesterPersonId = person.Id,
            StatusChangedUtc = now,
            StatusChangedByUserId = ParseUserIdOrNull(currentUserService.UserId)
        };
        await unitOfWork.GetRepository<CalendarEvent>().InsertAsync(calendarEvent);

        var actor = new CalendarActor(channel.Id, ParseUserIdOrNull(currentUserService.UserId), request.OriginIdentifier);
        await logWriter.WriteAsync(calendarEvent, CalendarEventLogAction.Created, actor,
            new CalendarEventLogChange(NewStartUtc: calendarEvent.StartUtc, NewEndUtc: calendarEvent.EndUtc, NewStatusCompanyId: calendarEvent.CalendarStatusCompanyId));

        // PersonPendingConfirmation.
        var pending = new PersonPendingConfirmation
        {
            CompanyId = companyId,
            PersonId = person.Id,
            Reason = request.Reason.Trim(),
            CommunicationChannelId = channel.Id,
            ChannelIdentifier = request.OriginIdentifier,
            RequestedUtc = now,
            Status = PersonPendingConfirmationStatus.Pending,
            ReviewEventId = calendarEvent.Id
        };
        await unitOfWork.GetRepository<PersonPendingConfirmation>().InsertAsync(pending);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<RegisterIntakePersonDto>
        {
            IsSuccess = true,
            Data = new RegisterIntakePersonDto(person.Id, true, pending.Id, calendarEvent.Id, true),
            Message = "Person registered and review activity created."
        };
    }

    private static CalendarActivityCompany BuildActivityCompany(PersonReviewRow row) => new()
    {
        CompanyId = Guid.Empty,
        CalendarActivityId = Guid.Empty,
        Name = string.Empty,
        Color = string.Empty,
        DefaultDurationMinutes = row.DefaultDurationMinutes,
        AllowsOverlap = row.AllowsOverlap,
        RequiresBusinessHours = row.RequiresBusinessHours,
        InitialCalendarStatusCompanyId = row.InitialCalendarStatusCompanyId,
        IsActive = true
    };

    private static string BuildReviewDescription(Person person, RegisterIntakePersonCommand request, string channelCode)
    {
        var lines = new List<string>
        {
            $"Persona: {person.FirstName} {person.LastName}".Trim(),
            $"Tipo: {person.PersonType}",
            $"Identificación: {person.IdentificationNumber}",
            person.GenderId.HasValue ? $"GenderId: {person.GenderId}" : "GenderId: (no indicado)",
            string.IsNullOrWhiteSpace(person.CommercialName) ? null : $"Nombre comercial: {person.CommercialName}",
            $"Canal de origen: {channelCode}",
            $"Identificador: {request.OriginIdentifier}",
            $"Motivo: {request.Reason}",
            $"Fecha de solicitud: {DateTime.UtcNow:O}",
            request.Phone is not null ? $"Teléfono: {request.Phone}" : null,
            request.Email is not null ? $"Correo: {request.Email}" : null
        };
        return string.Join('\n', lines.Where(l => l is not null));
    }

    private static CalendarEventOriginType ResolveOriginType(string channelCode, string? phone, string? email)
    {
        if (phone is not null && (string.Equals(channelCode, CommunicationChannelCodes.WhatsApp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(channelCode, CommunicationChannelCodes.Twilio, StringComparison.OrdinalIgnoreCase)
            || string.Equals(channelCode, CommunicationChannelCodes.Telegram, StringComparison.OrdinalIgnoreCase)))
        {
            return CalendarEventOriginType.Phone;
        }
        if (email is not null && (string.Equals(channelCode, CommunicationChannelCodes.SendGrid, StringComparison.OrdinalIgnoreCase)
            || string.Equals(channelCode, CommunicationChannelCodes.App, StringComparison.OrdinalIgnoreCase)))
        {
            return CalendarEventOriginType.Email;
        }
        if (phone is not null)
        {
            return CalendarEventOriginType.Phone;
        }
        if (email is not null)
        {
            return CalendarEventOriginType.Email;
        }
        return CalendarEventOriginType.System;
    }

    private static DateTime RoundUpToNext15Minutes(DateTime utcNow)
    {
        var ticksInQuarter = TimeSpan.FromMinutes(15).Ticks;
        var rounded = (utcNow.Ticks + ticksInQuarter - 1) / ticksInQuarter * ticksInQuarter;
        return new DateTime(rounded, DateTimeKind.Utc);
    }

    private static Guid? ParseUserIdOrNull(string? userId) =>
        Guid.TryParse(userId, out var parsed) && parsed != Guid.Empty ? parsed : null;

    private static async Task<ChannelRow?> FindChannelAsync(
        ISqlConnectionFactory factory,
        string channelCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, Code
              FROM Common.CommunicationChannels
             WHERE Code = @Code
               AND GcRecord = 0
               AND IsActive = TRUE
             LIMIT 1;
            """;
        using var connection = factory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<ChannelRow>(
            new CommandDefinition(sql, new { Code = channelCode.Trim().ToUpperInvariant() },
                cancellationToken: cancellationToken));
    }

    private sealed class PersonReviewRow
    {
        public Guid Id { get; init; }
        public int DefaultDurationMinutes { get; init; }
        public bool AllowsOverlap { get; init; }
        public bool RequiresBusinessHours { get; init; }
        public Guid InitialCalendarStatusCompanyId { get; init; }
    }

    private sealed class ChannelRow
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
    }
}