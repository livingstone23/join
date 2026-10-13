using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.ChannelIntake;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.UploadTicketDocument;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Support;
using MediatR;
using Microsoft.Extensions.Logging;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.RegisterInboundTicket;

/// <summary>
/// Handles <see cref="RegisterInboundTicketCommand"/> (SPEC 47 F4, "RegisterInboundTicketCommand — flujo completo").
/// <para>
/// There is no JWT on this route, so every read is scoped explicitly by the <c>CompanyId</c> of the command
/// (<c>GetAllIncludingDeletedAsync</c> with <c>CompanyId</c> and <c>GcRecord == 0</c>) instead of relying on the
/// tenant query filter. The webhook controllers also expose that tenant to <c>ICurrentUserService</c> for the
/// collaborators that read through it (<see cref="TicketInitialStatusResolver"/>, <see cref="TicketDtoAssembler"/>).
/// </para>
/// </summary>
public sealed class RegisterInboundTicketCommandHandler(
    IUnitOfWork unitOfWork,
    IFileStorageService fileStorageService,
    TicketDtoAssembler ticketDtoAssembler,
    TicketCodeGenerator ticketCodeGenerator,
    TicketInitialStatusResolver initialStatusResolver,
    IntakeRequesterResolver requesterResolver,
    IDatabaseErrorClassifier errorClassifier,
    ILogger<RegisterInboundTicketCommandHandler> logger)
    : IRequestHandler<RegisterInboundTicketCommand, Response<TicketDto>>
{
    /// <summary> Maximum length of <c>Ticket.Name</c>. </summary>
    public const int NameMaxLength = 150;

    /// <summary> Maximum length of <c>Ticket.Description</c>. </summary>
    public const int DescriptionMaxLength = 2000;

    private const int LogSummaryMaxLength = 2000;

    /// <inheritdoc />
    public async Task<Response<TicketDto>> Handle(RegisterInboundTicketCommand request, CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId;

        // --- 0. The inbound channel must still be active (it may have been rotated after the controller lookup). ---
        var inboundChannels = await unitOfWork.GetRepository<TicketInboundChannel>().GetAllIncludingDeletedAsync(x =>
            x.CompanyId == companyId && x.ChannelId == request.ChannelId && x.GcRecord == BaseAuditableEntity.ActiveGcRecord);
        var channel = await unitOfWork.GetRepository<CommunicationChannel>().GetAsync(request.ChannelId);

        if (!inboundChannels.Any() || channel is null)
        {
            return Response<TicketDto>.Error(
                TicketInboundErrorCodes.InboundChannelNotFound,
                ["No active inbound channel is configured for the company and channel."]);
        }

        // --- 1. Author: the active IsSuperAdminTicket of the company. ---
        var ticketAdminUserId = (await unitOfWork.GetRepository<TicketUserCompany>().GetAllIncludingDeletedAsync(x =>
                x.CompanyId == companyId && x.GcRecord == BaseAuditableEntity.ActiveGcRecord && x.IsSuperAdminTicket))
            .OrderBy(x => x.Created)
            .Select(x => (Guid?)x.UserId)
            .FirstOrDefault();

        if (ticketAdminUserId is not { } authorUserId)
        {
            logger.LogWarning(
                "[RegisterInboundTicket] Rejected inbound message {ExternalMessageId}: company {CompanyId} has no active IsSuperAdminTicket.",
                request.ExternalMessageId, companyId);
            return Response<TicketDto>.Error(
                TicketInboundErrorCodes.NoTicketAdmin,
                ["No active TicketUserCompany with IsSuperAdminTicket is configured for the company."]);
        }

        // --- 2. Idempotency: a provider retry returns the ticket already created. ---
        var existing = await FindExistingTicketAsync(request, cancellationToken);
        if (existing is not null)
        {
            return await BuildResponseAsync(existing, isDuplicate: true, cancellationToken);
        }

        // --- 3. Defaults: complexity and time unit from TicketCompanyDefault, status from TicketInitialStatusResolver (SPEC 42). ---
        var defaults = (await unitOfWork.GetRepository<TicketCompanyDefault>().GetAllIncludingDeletedAsync(x =>
                x.CompanyId == companyId && x.GcRecord == BaseAuditableEntity.ActiveGcRecord))
            .FirstOrDefault();

        if (defaults?.TicketStatusDefaultId is null
            || defaults.TicketComplexityDefaultId is not { } complexityId
            || defaults.TimeUnitDefaultId is not { } timeUnitId)
        {
            return DefaultsMissing(companyId, request, ["The company has no TicketCompanyDefault or its status, complexity or time unit default is missing."]);
        }

        var initialStatus = await initialStatusResolver.ResolveAsync(companyId, null, cancellationToken);
        if (!initialStatus.IsSuccess)
        {
            return DefaultsMissing(companyId, request, initialStatus.Errors?.ToList() ?? [initialStatus.Message]);
        }

        // --- 4. Requester: same normalization as the calendar agent (SPEC 45-post). Ambiguous = not linked. ---
        var senderIdentifier = request.SenderIdentifier.Trim();
        var match = await requesterResolver.LookupByChannelAsync(companyId, channel.Code ?? string.Empty, senderIdentifier, cancellationToken);
        var personId = match.Found ? match.PersonId : null;

        // --- 5..8. Build the ticket. ---
        var sender = string.IsNullOrWhiteSpace(request.SenderDisplayName) ? senderIdentifier : request.SenderDisplayName.Trim();
        var ticket = new Ticket
        {
            CompanyId = companyId,
            ChannelId = request.ChannelId,
            ExternalMessageId = request.ExternalMessageId.Trim(),
            TicketStatusId = initialStatus.Data,
            TicketComplexityId = complexityId,
            TimeUnitId = timeUnitId,
            AreaId = defaults.AreaDefaultId,
            ProjectId = defaults.ProjectDefaultId,
            PersonId = personId,
            CreatedByUserId = authorUserId,
            AssignedToUserId = null,
            Name = BuildName(request, sender),
            Description = BuildDescription(request, personId is null ? $"Remitente: {sender} ({senderIdentifier})\n\n" : null),
            IsVisibleToExternals = true
        };

        await ticketCodeGenerator.AssignCodeAsync(ticket, DateTime.UtcNow, cancellationToken);
        ticket.AddLog(authorUserId, LogType.Creation, $"Ticket creado automáticamente desde {channel.Name} ({sender})");

        // --- 9. Persist. A concurrent retry that committed first wins: answer with its ticket. ---
        try
        {
            await unitOfWork.GetRepository<Ticket>().InsertAsync(ticket);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (errorClassifier.IsUniqueViolation(exception, TicketInboundErrorCodes.ExternalMessageIdIndex))
        {
            var winner = await FindExistingTicketAsync(request, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return await BuildResponseAsync(winner, isDuplicate: true, cancellationToken);
        }

        // --- 10. Attachments, best-effort: a rejected file is skipped and noted, never fails the ticket. ---
        await PersistAttachmentsAsync(ticket, authorUserId, request, cancellationToken);

        // --- 11. Response. ---
        return await BuildResponseAsync(ticket, isDuplicate: false, cancellationToken);
    }

    private Response<TicketDto> DefaultsMissing(Guid companyId, RegisterInboundTicketCommand request, List<string> errors)
    {
        logger.LogWarning(
            "[RegisterInboundTicket] Rejected inbound message {ExternalMessageId}: company {CompanyId} has incomplete ticket defaults ({Errors}).",
            request.ExternalMessageId, companyId, string.Join("; ", errors));
        return Response<TicketDto>.Error(TicketInboundErrorCodes.DefaultsMissing, errors);
    }

    private async Task<Ticket?> FindExistingTicketAsync(RegisterInboundTicketCommand request, CancellationToken cancellationToken)
    {
        var externalMessageId = request.ExternalMessageId.Trim();
        var matches = await unitOfWork.GetRepository<Ticket>().GetAllIncludingDeletedAsync(t =>
            t.CompanyId == request.CompanyId
            && t.ChannelId == request.ChannelId
            && t.ExternalMessageId == externalMessageId
            && t.GcRecord == BaseAuditableEntity.ActiveGcRecord);

        return matches.FirstOrDefault();
    }

    /// <summary>
    /// Validates each attachment with the same rules as <c>UploadTicketDocumentCommandHandler</c> (SPEC 36: type,
    /// whitelist, size, files per ticket, daily quota), stores the accepted ones anchored to the <c>Creation</c> log
    /// and records the skipped ones (including <see cref="RegisterInboundTicketCommand.SkippedAttachments"/>) in an
    /// internal note visible only to the creator and the assignee.
    /// </summary>
    private async Task PersistAttachmentsAsync(Ticket ticket, Guid authorUserId, RegisterInboundTicketCommand request, CancellationToken cancellationToken)
    {
        var skipped = new List<string>(request.SkippedAttachments ?? []);

        if (request.Attachments.Count > 0)
        {
            var settings = (await unitOfWork.GetRepository<TicketAttachmentSettingsEntity>().GetAllIncludingDeletedAsync(x =>
                    x.CompanyId == ticket.CompanyId && x.GcRecord == BaseAuditableEntity.ActiveGcRecord))
                .FirstOrDefault();

            if (settings is null)
            {
                skipped.AddRange(request.Attachments.Select(a => $"{a.FileName}: la empresa no tiene configuración de adjuntos"));
            }
            else
            {
                await StoreAcceptedAttachmentsAsync(ticket, settings, request.Attachments, skipped, cancellationToken);
            }
        }

        if (skipped.Count > 0)
        {
            var summary = "Adjuntos omitidos al registrar el ticket:\n- " + string.Join("\n- ", skipped);
            ticket.AddLog(authorUserId, LogType.InternalNote, Truncate(summary, LogSummaryMaxLength), isOnlyForCreatedAndAssigned: true);
            // The ticket is already tracked: UpdateAsync marks the new log as Added (its client-side Id would make
            // change detection treat it as an existing row).
            await unitOfWork.GetRepository<Ticket>().UpdateAsync(ticket);
            logger.LogWarning(
                "[RegisterInboundTicket] Ticket {TicketId}: skipped {SkippedCount} attachment(s).",
                ticket.Id, skipped.Count);
        }

        if (skipped.Count > 0 || request.Attachments.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task StoreAcceptedAttachmentsAsync(
        Ticket ticket,
        TicketAttachmentSettingsEntity settings,
        IReadOnlyList<InboundAttachment> attachments,
        List<string> skipped,
        CancellationToken cancellationToken)
    {
        var creationLogId = ticket.TicketLogs.First(l => l.LogType == LogType.Creation).Id;
        var documentRepository = unitOfWork.GetRepository<TicketDocument>();
        var todayUtc = DateTime.UtcNow.Date;
        var storedToday = settings.MaxFilesPerDay.HasValue
            ? (await documentRepository.GetAllIncludingDeletedAsync(d =>
                d.CompanyId == ticket.CompanyId && d.GcRecord == BaseAuditableEntity.ActiveGcRecord && d.Created >= todayUtc)).Count()
            : 0;
        var storedForTicket = 0;

        foreach (var attachment in attachments)
        {
            var extension = Path.GetExtension(attachment.FileName)?.ToLowerInvariant() ?? string.Empty;
            string? reason = null;

            if (!UploadTicketDocumentCommandHandler.TryMapExtension(extension, out var documentType))
            {
                reason = $"extensión '{extension}' no soportada";
            }
            else if (!settings.AllowedDocumentTypes.HasFlag(documentType))
            {
                reason = $"tipo '{documentType}' no permitido";
            }
            else if (attachment.Length > settings.MaxFileSizeBytes)
            {
                reason = $"{attachment.Length} bytes supera el máximo de {settings.MaxFileSizeBytes}";
            }
            else if (storedForTicket >= settings.MaxFilesPerTicket)
            {
                reason = $"se alcanzó el máximo de {settings.MaxFilesPerTicket} adjuntos por ticket";
            }
            else if (settings.MaxFilesPerDay is { } maxPerDay && storedToday >= maxPerDay)
            {
                reason = $"se alcanzó la cuota diaria de {maxPerDay} adjuntos";
            }

            if (reason is not null)
            {
                skipped.Add($"{attachment.FileName}: {reason}");
                continue;
            }

            var newName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
            var storageKey = $"{ticket.CompanyId}/{ticket.Id}/{newName}";

            try
            {
                var saveResult = await fileStorageService.SaveAsync(attachment.Content, storageKey, attachment.ContentType, cancellationToken);

                await documentRepository.InsertAsync(new TicketDocument
                {
                    TicketId = ticket.Id,
                    CompanyId = ticket.CompanyId,
                    TicketLogsId = creationLogId,
                    DocumentType = documentType,
                    OriginalName = attachment.FileName,
                    NewName = newName,
                    Path = storageKey,
                    StorageProvider = StorageProviderKind.Local,
                    ContentType = attachment.ContentType,
                    SizeBytes = saveResult.SizeBytes
                });

                storedForTicket++;
                storedToday++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "[RegisterInboundTicket] Storage failed for attachment {FileName} of ticket {TicketId}.", attachment.FileName, ticket.Id);
                skipped.Add($"{attachment.FileName}: falló el almacenamiento");
            }
        }
    }

    private async Task<Response<TicketDto>> BuildResponseAsync(Ticket ticket, bool isDuplicate, CancellationToken cancellationToken)
    {
        var company = await unitOfWork.GetRepository<Company>().GetAsync(ticket.CompanyId);
        var dto = await ticketDtoAssembler.BuildAsync(ticket, company!, cancellationToken);

        return new Response<TicketDto>
        {
            IsSuccess = true,
            Message = isDuplicate ? TicketInboundErrorCodes.Duplicate : "Ticket registered successfully.",
            Data = dto
        };
    }

    /// <summary> Subject, else the first characters of the body (on one line), else "Mensaje de {sender}". </summary>
    private static string BuildName(RegisterInboundTicketCommand request, string sender)
    {
        var source = !string.IsNullOrWhiteSpace(request.MessageSubject) ? request.MessageSubject : request.MessageBody;
        var singleLine = string.Join(' ', (source ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return Truncate(string.IsNullOrEmpty(singleLine) ? $"Mensaje de {sender}" : singleLine, NameMaxLength);
    }

    private static string BuildDescription(RegisterInboundTicketCommand request, string? senderPrefix) =>
        Truncate($"{senderPrefix}{request.MessageBody?.Trim()}", DescriptionMaxLength);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
