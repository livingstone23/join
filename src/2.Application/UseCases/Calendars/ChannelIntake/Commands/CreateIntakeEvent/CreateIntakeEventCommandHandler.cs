using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.CreateIntakeEvent;

/// <summary>
/// Handles <see cref="CreateIntakeEventCommand"/> (SPEC 45):
/// <list type="number">
///   <item>An activity with the same <c>(CompanyId, OriginChannelId, OriginReference)</c> answers
///   <c>IsDuplicate = true</c> without creating anything. Two concurrent retries both pass that check; the loser hits
///   the unique index (<see cref="OriginReferenceIndex"/>) and is answered the same way.</item>
///   <item>The requester must be an active person of the company (<c>CALENDAR_INVALID_LINK</c>).</item>
///   <item>Recipient: the user whose e-mail matches exactly (active in the company, with a calendar); otherwise the
///   company's default user, with a note in the description when an e-mail did not match.</item>
///   <item>The activity is created by <see cref="CalendarEventCommandProcessor"/> in the recipient's default calendar,
///   with the type's initial status, R4 + R5 (the agent is never the owner) and the requester's channel in the log.</item>
/// </list>
/// </summary>
public sealed class CreateIntakeEventCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ISqlConnectionFactory connectionFactory,
    CalendarCompanyGuard companyGuard,
    CalendarCatalogReader catalogReader,
    IntakeChannelReader channelReader,
    CalendarEventCommandProcessor processor,
    IDatabaseErrorClassifier errorClassifier)
    : IRequestHandler<CreateIntakeEventCommand, Response<CreateIntakeEventDto>>
{
    /// <summary> Unique index on <c>(CompanyId, OriginChannelId, OriginReference)</c> of <c>calendar.events</c>. </summary>
    public const string OriginReferenceIndex = "ux_calendarevents_company_channel_originreference_active";

    // Read on its own connection: after a unique violation the EF transaction is aborted and cannot be queried.
    private const string DuplicateSql = """
        SELECT e.id AS Id, e.owneruserid AS OwnerUserId, e.startutc AS StartUtc, e.endutc AS EndUtc,
               sc.id AS StatusId, s.code AS StatusCode, sc.name AS StatusName, sc.color AS StatusColor,
               TRIM(CONCAT(u.firstname, ' ', COALESCE(u.lastname, ''))) AS OwnerFullName
          FROM calendar.events e
          INNER JOIN calendar.statuscompanies sc ON sc.id = e.calendarstatuscompanyid
          INNER JOIN calendar.statuses s ON s.id = sc.calendarstatusid
          INNER JOIN security.users u ON u.id = e.owneruserid
         WHERE e.companyid = @TenantId
           AND e.gcrecord = 0
           AND e.originchannelid = @ChannelId
           AND e.originreference = @Reference
         LIMIT 1;
        """;

    public async Task<Response<CreateIntakeEventDto>> Handle(CreateIntakeEventCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CreateIntakeEventDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyResponse = await companyGuard.GetContextAsync(companyId, cancellationToken);
        if (!companyResponse.IsSuccess)
        {
            return Response<CreateIntakeEventDto>.Error(companyResponse.Message, companyResponse.Errors);
        }

        var company = companyResponse.Data!;
        var channel = await channelReader.FindActiveAsync(request.OriginChannelCode);
        if (channel is null)
        {
            return Response<CreateIntakeEventDto>.Error(CalendarErrorCodes.IntakeInvalidChannel,
                [$"The channel '{request.OriginChannelCode}' is not an active agent channel."]);
        }

        var reference = request.OriginReference.Trim();
        var duplicate = await FindDuplicateAsync(companyId, channel.Id, reference, company, cancellationToken);
        if (duplicate is not null)
        {
            return duplicate;
        }

        if (!await IsActivePersonAsync(companyId, request.RequesterPersonId))
        {
            return Response<CreateIntakeEventDto>.Error(CalendarErrorCodes.InvalidLink, ["requesterPersonId is not an active person of the current company."]);
        }

        var description = request.Description;
        Guid? matchedUserId = null;
        if (!string.IsNullOrWhiteSpace(request.TargetUserEmail))
        {
            matchedUserId = await FindTargetUserAsync(companyId, request.TargetUserEmail);
            if (matchedUserId is null)
            {
                var note = $"El solicitante indicó ser atendido por: {request.TargetUserEmail.Trim()} (no corresponde a un usuario de la empresa).";
                description = string.IsNullOrWhiteSpace(description) ? note : $"{description.Trim()}\n\n{note}";
            }
        }

        var ownerUserId = matchedUserId ?? company.DefaultActivityUserId;
        if (matchedUserId is null && !await HasCalendarAsync(companyId, ownerUserId))
        {
            return Response<CreateIntakeEventDto>.Error(CalendarErrorCodes.IntakeTargetUserHasNoCalendar,
                ["The default activity user of the company has no calendar."]);
        }

        var activity = await catalogReader.GetUsableActivityByCodeAsync(companyId, request.ActivityCode);
        if (activity is null)
        {
            return Response<CreateIntakeEventDto>.Error(CalendarErrorCodes.ActivityNotEnabled,
                [$"The activity type '{request.ActivityCode}' is not enabled in the company."]);
        }

        var input = new IntakeEventInput
        {
            CalendarActivityCompanyId = activity.ActivityCompany.Id,
            Title = request.Title,
            Summary = request.Summary,
            Description = description,
            Location = request.Location,
            VideoCallUrl = request.VideoCallUrl,
            IsAllDay = request.IsAllDay,
            Start = request.Start,
            End = request.End,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            RecurrenceRule = request.RecurrenceRule,
            RequesterPersonId = request.RequesterPersonId
        };
        var originType = request.OriginType is not null && Enum.TryParse<CalendarEventOriginType>(request.OriginType, true, out var parsed)
            ? parsed
            : IntakeChannels.OriginTypeOf(channel.Code);
        var origin = new CalendarExternalOrigin(channel.Id, originType, request.OriginIdentifier.Trim(), reference);

        Response<CalendarEventDto> created;
        try
        {
            created = await processor.CreateAsync(input, ownerUserId, cancellationToken, origin);
        }
        catch (Exception exception) when (errorClassifier.IsUniqueViolation(exception, OriginReferenceIndex))
        {
            // A concurrent retry committed first. Nothing of this request is kept (its transaction is aborted).
            var winner = await FindDuplicateAsync(companyId, channel.Id, reference, company, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return winner;
        }

        if (!created.IsSuccess)
        {
            return Response<CreateIntakeEventDto>.Error(created.Message, created.Errors);
        }

        var dto = created.Data!;
        return new Response<CreateIntakeEventDto>
        {
            IsSuccess = true,
            Data = new CreateIntakeEventDto(
                dto.Id,
                IsDuplicate: false,
                new IntakeStatusDto(dto.Status.Id, dto.Status.Code, dto.Status.Name, dto.Status.Color),
                dto.OwnerName,
                dto.StartUtc,
                dto.EndUtc,
                dto.TimeZone,
                RoutedToDefaultUser: matchedUserId is null),
            Message = "Calendar activity created successfully."
        };
    }

    private async Task<Response<CreateIntakeEventDto>?> FindDuplicateAsync(
        Guid companyId,
        Guid channelId,
        string reference,
        CalendarCompanyContext company,
        CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<DuplicateRow>(
            new CommandDefinition(DuplicateSql, new { TenantId = companyId, ChannelId = channelId, Reference = reference }, cancellationToken: cancellationToken));

        return row is null
            ? null
            : new Response<CreateIntakeEventDto>
            {
                IsSuccess = true,
                Data = new CreateIntakeEventDto(
                    row.Id,
                    IsDuplicate: true,
                    new IntakeStatusDto(row.StatusId, row.StatusCode, row.StatusName, row.StatusColor),
                    row.OwnerFullName,
                    CalendarTime.AsUtc(row.StartUtc),
                    CalendarTime.AsUtc(row.EndUtc),
                    company.TimeZoneId,
                    RoutedToDefaultUser: row.OwnerUserId == company.DefaultActivityUserId),
                Message = "The request was already registered; the existing activity is returned."
            };
    }

    private async Task<bool> IsActivePersonAsync(Guid companyId, Guid personId) =>
        (await unitOfWork.GetRepository<Person>()
            .GetAllIncludingDeletedAsync(x => x.Id == personId && x.CompanyId == companyId && x.GcRecord == 0 && x.IsActive))
        .Any();

    /// <summary>
    /// Exact e-mail (without case) of an active user that belongs to the company and has a calendar there.
    /// </summary>
    private async Task<Guid?> FindTargetUserAsync(Guid companyId, string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var userIds = (await unitOfWork.GetRepository<ApplicationUser>()
                .GetAllIncludingDeletedAsync(x => x.Email != null && x.Email.ToLower() == normalized && x.IsActive && x.GcRecord == 0))
            .Select(x => x.Id)
            .ToList();

        foreach (var userId in userIds)
        {
            var member = (await unitOfWork.GetRepository<UserCompany>()
                    .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.UserId == userId && x.GcRecord == 0))
                .Any();
            if (member && await HasCalendarAsync(companyId, userId))
            {
                return userId;
            }
        }

        return null;
    }

    private async Task<bool> HasCalendarAsync(Guid companyId, Guid userId) =>
        (await unitOfWork.GetRepository<CalendarUserConfiguration>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.UserId == userId && x.GcRecord == 0))
        .Any();

    private sealed class DuplicateRow
    {
        public Guid Id { get; init; }
        public Guid OwnerUserId { get; init; }
        public DateTime StartUtc { get; init; }
        public DateTime EndUtc { get; init; }
        public Guid StatusId { get; init; }
        public string StatusCode { get; init; } = string.Empty;
        public string StatusName { get; init; } = string.Empty;
        public string StatusColor { get; init; } = string.Empty;
        public string OwnerFullName { get; init; } = string.Empty;
    }
}
