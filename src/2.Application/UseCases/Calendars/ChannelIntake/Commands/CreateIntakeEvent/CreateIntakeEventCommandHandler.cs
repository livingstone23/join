using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// Handles <see cref="CreateIntakeEventCommand"/> (SPEC 45 F4). Deduplicates by
/// <c>(OriginChannelId, OriginReference)</c> before any insert and answers <c>IsDuplicate = true</c> when the
/// pair exists or the unique-index race fires (<c>23505</c>, <c>IX_CalendarEvents_Company_OriginReference</c>).
/// Resolves the recipient (target e-mail match → user's default calendar, otherwise the company's default
/// user) and applies R4 + R5 from SPEC 44.
/// </summary>
public sealed class CreateIntakeEventCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ISqlConnectionFactory connectionFactory,
    CalendarCompanyGuard companyGuard,
    CalendarEventScheduleGuard scheduleGuard,
    CalendarEventLogWriter logWriter,
    CalendarEventInitialStatusResolver statusResolver)
    : IRequestHandler<CreateIntakeEventCommand, Response<CreateIntakeEventDto>>
{
    private const string ActivityByCodeSql = """
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

    private const string DuplicateSql = """
        SELECT e.Id, e.StartUtc, e.EndUtc, e.CalendarStatusCompanyId, e.OwnerUserId,
               s.Code AS StatusCode, sc.Name AS StatusName, sc.Color AS StatusColor
          FROM Calendar.Events e
          INNER JOIN Calendar.StatusCompanies sc ON sc.Id = e.CalendarStatusCompanyId
          INNER JOIN Calendar.Statuses s ON s.Id = sc.CalendarStatusId
         WHERE e.CompanyId = @TenantId
           AND e.GcRecord = 0
           AND e.OriginChannelId = @OriginChannelId
           AND e.OriginReference = @OriginReference
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

    private const string RequesterPersonSql = """
        SELECT p.Id
          FROM admin.persons p
         WHERE p.companyid = @TenantId
           AND p.gcrecord = 0
           AND p.isactive = TRUE
           AND p.id = @PersonId
         LIMIT 1;
        """;

    private const string TargetUserSql = """
        SELECT u.Id
          FROM Security.Users u
          INNER JOIN Security.UserCompanies uc ON uc.UserId = u.Id AND uc.CompanyId = @TenantId AND uc.GcRecord = 0
          INNER JOIN Calendar.UserConfigurations c ON c.UserId = u.Id AND c.CompanyId = @TenantId AND c.GcRecord = 0
         WHERE u.GcRecord = 0 AND u.IsActive = TRUE
           AND LOWER(TRIM(u.Email)) = LOWER(TRIM(@Email))
         LIMIT 1;
        """;

    private const string OwnerFullNameSql = """
        SELECT u.FirstName || ' ' || u.LastName
          FROM Security.Users u
         WHERE u.Id = @UserId
         LIMIT 1;
        """;

    public async Task<Response<CreateIntakeEventDto>> Handle(
        CreateIntakeEventCommand request,
        CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId == Guid.Empty ? currentUserService.CompanyId : request.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CreateIntakeEventDto>.Error(CalendarErrorCodes.CompanyRequired,
                ["The X-Company-Id header or the CompanyId claim is required."]);
        }

        var companyResponse = await companyGuard.GetContextAsync(companyId, cancellationToken);
        if (!companyResponse.IsSuccess)
        {
            return Response<CreateIntakeEventDto>.Error(companyResponse.Message, companyResponse.Errors);
        }
        var company = companyResponse.Data!;

        var channel = await FindChannelAsync(connectionFactory, request.OriginChannelCode, cancellationToken);
        if (channel is null)
        {
            return Response<CreateIntakeEventDto>.Error(CalendarErrorCodes.IntakeInvalidChannel,
                [$"Channel '{request.OriginChannelCode}' is not registered or is inactive."]);
        }

        using var connection = connectionFactory.CreateConnection();

        // Deduplication: same (OriginChannelId, OriginReference) — answered idempotently.
        var existing = await connection.QueryFirstOrDefaultAsync<DuplicateRow>(
            new CommandDefinition(DuplicateSql,
                new { TenantId = companyId, OriginChannelId = channel.Id, OriginReference = request.OriginReference },
                cancellationToken: cancellationToken));
        if (existing is not null)
        {
            var ownerName = await connection.ExecuteScalarAsync<string>(
                new CommandDefinition(OwnerFullNameSql, new { UserId = existing.OwnerUserId },
                    cancellationToken: cancellationToken)) ?? string.Empty;
            return new Response<CreateIntakeEventDto>
            {
                IsSuccess = true,
                Data = new CreateIntakeEventDto(
                    existing.Id,
                    IsDuplicate: true,
                    Status: new IntakeStatusDto(existing.CalendarStatusCompanyId, existing.StatusCode, existing.StatusName, existing.StatusColor),
                    OwnerFullName: ownerName.Trim(),
                    StartUtc: existing.StartUtc,
                    EndUtc: existing.EndUtc,
                    TimeZone: company.TimeZoneId,
                    RoutedToDefaultUser: false),
                Message = "Duplicate origin reference; existing event returned."
            };
        }

        // Requester person must exist and be active in the company.
        var requesterOk = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(RequesterPersonSql,
                new { TenantId = companyId, PersonId = request.RequesterPersonId },
                cancellationToken: cancellationToken));
        if (!requesterOk)
        {
            return Response<CreateIntakeEventDto>.Error(CalendarErrorCodes.InvalidLink,
                ["requesterPersonId does not exist in the current company."]);
        }

        // Recipient resolution.
        var routedToDefault = false;
        var recipientUserId = company.DefaultActivityUserId;
        var description = request.Description;
        if (!string.IsNullOrWhiteSpace(request.TargetUserEmail))
        {
            var matched = await connection.QueryFirstOrDefaultAsync<Guid?>(
                new CommandDefinition(TargetUserSql,
                    new { TenantId = companyId, Email = request.TargetUserEmail.Trim() },
                    cancellationToken: cancellationToken));
            if (matched.HasValue && matched.Value != Guid.Empty)
            {
                recipientUserId = matched.Value;
            }
            else
            {
                routedToDefault = true;
                description = AppendUnmatchedEmailNote(description, request.TargetUserEmail);
            }
        }
        else
        {
            routedToDefault = true;
        }

        var defaultCalendarId = await connection.QueryFirstOrDefaultAsync<Guid?>(
            new CommandDefinition(UserConfigSql, new { TenantId = companyId, UserId = recipientUserId },
                cancellationToken: cancellationToken));
        if (defaultCalendarId is null)
        {
            return Response<CreateIntakeEventDto>.Error(CalendarErrorCodes.IntakeTargetUserHasNoCalendar,
                ["The recipient user has no calendar configuration in this company."]);
        }

        // Activity type by code.
        var activity = await connection.QueryFirstOrDefaultAsync<ActivityRow>(
            new CommandDefinition(ActivityByCodeSql, new { TenantId = companyId, Code = request.ActivityCode.Trim().ToUpperInvariant() },
                cancellationToken: cancellationToken));
        if (activity is null)
        {
            return Response<CreateIntakeEventDto>.Error(CalendarErrorCodes.ActivityNotEnabled,
                [$"Activity '{request.ActivityCode}' is not enabled in the company."]);
        }

        // Resolve times (R3: company-zone aware, all-day spans midnight boundaries).
        var times = CalendarEventCommandProcessor.ResolveTimes(BuildInputLike(request), company, activity.DefaultDurationMinutes);
        if (!times.IsSuccess)
        {
            return Response<CreateIntakeEventDto>.Error(times.Message, times.Errors);
        }
        var startUtc = CalendarTime.AsUtc(times.Data.StartUtc);
        var endUtc = CalendarTime.AsUtc(times.Data.EndUtc);

        // Initial status.
        var activityCompany = new CalendarActivityCompany
        {
            CompanyId = companyId,
            CalendarActivityId = Guid.Empty,
            Name = string.Empty,
            Color = string.Empty,
            DefaultDurationMinutes = activity.DefaultDurationMinutes,
            AllowsOverlap = activity.AllowsOverlap,
            RequiresBusinessHours = activity.RequiresBusinessHours,
            InitialCalendarStatusCompanyId = activity.InitialCalendarStatusCompanyId,
            IsActive = true
        };
        var statusResponse = await statusResolver.ResolveAsync(activityCompany);
        if (!statusResponse.IsSuccess)
        {
            return Response<CreateIntakeEventDto>.Error(statusResponse.Message, statusResponse.Errors);
        }
        var status = statusResponse.Data!;
        var statusGlobal = await unitOfWork.GetRepository<Domain.Calendars.CalendarStatus>().GetAsync(status.CalendarStatusId);
        var blocksTime = statusGlobal?.BlocksTime ?? true;

        // Schedule guard (creator ≠ owner → R4 + R5 apply).
        var candidates = new[] { new CalendarOccurrence(startUtc, endUtc) };
        var scheduleCheck = new CalendarScheduleCheck(
            company,
            recipientUserId,
            ActorIsOwner: false,
            activity.AllowsOverlap,
            activity.RequiresBusinessHours,
            blocksTime,
            IsAllDay: request.IsAllDay,
            candidates,
            new HashSet<Guid>(),
            new HashSet<(Guid, DateTime)>());
        var validation = await scheduleGuard.ValidateAsync(scheduleCheck, cancellationToken);
        if (!validation.IsSuccess)
        {
            return Response<CreateIntakeEventDto>.Error(validation.Message, validation.Errors);
        }

        // Insert the CalendarEvent directly (the processor's CreateAsync forces OriginType = User, which is
        // wrong for the agent's intake flow).
        var now = DateTime.UtcNow;
        var actorUserId = ParseUserIdOrNull(currentUserService.UserId);
        var calendarEvent = new CalendarEvent
        {
            CompanyId = companyId,
            CalendarId = defaultCalendarId.Value,
            OwnerUserId = recipientUserId,
            CalendarActivityCompanyId = activity.Id,
            CalendarStatusCompanyId = status.Id,
            Title = request.Title.Trim(),
            Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim(),
            VideoCallUrl = string.IsNullOrWhiteSpace(request.VideoCallUrl) ? null : request.VideoCallUrl.Trim(),
            IsAllDay = request.IsAllDay,
            StartUtc = startUtc,
            EndUtc = endUtc,
            OriginChannelId = channel.Id,
            OriginType = ResolveOriginType(request.OriginChannelCode, request.OriginType),
            OriginIdentifier = request.OriginIdentifier,
            OriginReference = request.OriginReference,
            OriginUserId = actorUserId,
            RequesterPersonId = request.RequesterPersonId,
            StatusChangedUtc = now,
            StatusChangedByUserId = actorUserId
        };
        await unitOfWork.GetRepository<CalendarEvent>().InsertAsync(calendarEvent);

        var actor = new CalendarActor(channel.Id, actorUserId, request.OriginIdentifier);
        await logWriter.WriteAsync(calendarEvent, CalendarEventLogAction.Created, actor,
            new CalendarEventLogChange(NewStartUtc: calendarEvent.StartUtc, NewEndUtc: calendarEvent.EndUtc, NewStatusCompanyId: calendarEvent.CalendarStatusCompanyId));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var ownerFullName = await connection.ExecuteScalarAsync<string>(
            new CommandDefinition(OwnerFullNameSql, new { UserId = recipientUserId },
                cancellationToken: cancellationToken)) ?? string.Empty;

        return new Response<CreateIntakeEventDto>
        {
            IsSuccess = true,
            Data = new CreateIntakeEventDto(
                calendarEvent.Id,
                IsDuplicate: false,
                Status: new IntakeStatusDto(status.Id, statusGlobal?.Code ?? string.Empty, status.Name, status.Color),
                OwnerFullName: ownerFullName.Trim(),
                StartUtc: startUtc,
                EndUtc: endUtc,
                TimeZone: company.TimeZoneId,
                RoutedToDefaultUser: routedToDefault),
            Message = "Calendar activity created."
        };
    }

    private static IntakeEventInput BuildInputLike(CreateIntakeEventCommand request) => new()
    {
        IsAllDay = request.IsAllDay,
        Start = request.Start,
        End = request.End,
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        RecurrenceRule = request.RecurrenceRule
    };

    private static string AppendUnmatchedEmailNote(string? description, string email) =>
        string.IsNullOrWhiteSpace(description)
            ? $"El solicitante indicó ser atendido por: {email} (no corresponde a un usuario de la empresa)."
            : $"{description}\n\nEl solicitante indicó ser atendido por: {email} (no corresponde a un usuario de la empresa).";

    private static CalendarEventOriginType ResolveOriginType(string channelCode, string requestedOriginType)
    {
        if (Enum.TryParse<CalendarEventOriginType>(requestedOriginType, true, out var parsed))
        {
            return parsed;
        }
        if (string.Equals(channelCode, CommunicationChannelCodes.SendGrid, StringComparison.OrdinalIgnoreCase)
            || string.Equals(channelCode, CommunicationChannelCodes.App, StringComparison.OrdinalIgnoreCase))
        {
            return CalendarEventOriginType.Email;
        }
        if (string.Equals(channelCode, CommunicationChannelCodes.WhatsApp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(channelCode, CommunicationChannelCodes.Twilio, StringComparison.OrdinalIgnoreCase)
            || string.Equals(channelCode, CommunicationChannelCodes.Telegram, StringComparison.OrdinalIgnoreCase))
        {
            return CalendarEventOriginType.Phone;
        }
        return CalendarEventOriginType.System;
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

    private sealed class ActivityRow
    {
        public Guid Id { get; init; }
        public int DefaultDurationMinutes { get; init; }
        public bool AllowsOverlap { get; init; }
        public bool RequiresBusinessHours { get; init; }
        public Guid InitialCalendarStatusCompanyId { get; init; }
    }

    private sealed class DuplicateRow
    {
        public Guid Id { get; init; }
        public DateTime StartUtc { get; init; }
        public DateTime EndUtc { get; init; }
        public Guid CalendarStatusCompanyId { get; init; }
        public Guid OwnerUserId { get; init; }
        public string StatusCode { get; init; } = string.Empty;
        public string StatusName { get; init; } = string.Empty;
        public string StatusColor { get; init; } = string.Empty;
    }

    private sealed class ChannelRow
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
    }

    private sealed record IntakeEventInput : ICalendarEventInput
    {
        public bool IsAllDay { get; init; }
        public DateTimeOffset? Start { get; init; }
        public DateTimeOffset? End { get; init; }
        public DateOnly? StartDate { get; init; }
        public DateOnly? EndDate { get; init; }
        public string? RecurrenceRule { get; init; }
        public Guid? CalendarId => null;
        public Guid CalendarActivityCompanyId => Guid.Empty;
        public string Title => string.Empty;
        public string? Summary => null;
        public string? Description => null;
        public string? Location => null;
        public string? VideoCallUrl => null;
        public Guid? RequesterPersonId => null;
        public Guid? CustomerId => null;
        public Guid? TicketId => null;
        public Guid? ProjectId => null;
    }
}