using System;
using System.IO;
using System.Linq;
using System.Text;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Support;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JOIN.Application.UseCases.Messaging.Tickets.RegisterInboundTicket;

/// <summary>
/// Maneja la creación de un ticket a partir de un mensaje entrante (SPEC 47 F4).
/// El flujo completo aparece en la sección "RegisterInboundTicketCommand — flujo
/// completo" del spec; lo resumo aquí en el mismo orden:
/// <list type="number">
/// <item>Lookup por <c>(CompanyId, ChannelId, ExternalMessageId)</c> — dedup. Si existe, devolverlo.</item>
/// <item>Resolver <c>TicketUserCompany.IsSuperAdminTicket</c> activo — sin él, ningún ticket puede atribuirse.</item>
/// <item>Cargar <c>TicketCompanyDefault</c> con los tres defaults obligatorios (status / complexity / time unit).</item>
/// <item>Buscar <c>PersonContact</c> por <c>SenderIdentifier</c> para resolver <c>PersonId</c>; si no hay match, dejar el remitente en el <c>Description</c>.</item>
/// <item>Generar código vía <see cref="TicketCodeGenerator"/>.</item>
/// <item>Persistir ticket + log <c>Creation</c> + adjuntos (best-effort, ningún adjunto falla el alta).</item>
/// <item>Cubrir la carrera de dedup contra el índice único filtrado (PostgresException 23505).</item>
/// </list>
/// </summary>
public sealed class RegisterInboundTicketCommandHandler(
    IUnitOfWork unitOfWork,
    IFileStorageService fileStorageService,
    TicketDtoAssembler ticketDtoAssembler,
    TicketCodeGenerator ticketCodeGenerator,
    ILogger<RegisterInboundTicketCommandHandler> logger)
    : IRequestHandler<RegisterInboundTicketCommand, Response<TicketDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly IFileStorageService _fileStorageService = fileStorageService;
    private readonly TicketDtoAssembler _ticketDtoAssembler = ticketDtoAssembler;
    private readonly TicketCodeGenerator _ticketCodeGenerator = ticketCodeGenerator;
    private readonly ILogger<RegisterInboundTicketCommandHandler> _logger = logger;

    /// <inheritdoc />
    public async Task<Response<TicketDto>> Handle(RegisterInboundTicketCommand request, CancellationToken cancellationToken)
    {
        // --- 1. Confirm a TicketInboundChannel exists for (CompanyId, ChannelId) (handler trust boundary).
        // El controller ya validó el token; acá sólo verificamos que la fila siga activa.
        var inboundRepository = _unitOfWork.GetRepository<TicketInboundChannel>();
        var allInbound = await inboundRepository.GetAllAsync();
        var inboundActive = allInbound.Any(x =>
            x.CompanyId == request.CompanyId
            && x.ChannelId == request.ChannelId
            && x.GcRecord == BaseAuditableEntity.ActiveGcRecord);

        if (!inboundActive)
        {
            return Response<TicketDto>.Error(
                TicketInboundErrorCodes.InvalidToken,
                ["No active inbound channel is configured for the supplied company + channel."]);
        }

        // --- 2. Dedup: existing ticket? ---
        var dedup = await TryFindExistingTicket(request, cancellationToken);
        if (dedup is { } existingTicket)
        {
            return await BuildResponseAsync(existingTicket, isDuplicate: true, cancellationToken);
        }

        // --- 2. Resolve IsSuperAdminTicket for the tenant ---
        var ticketAdminUserId = await ResolveSuperAdminUserIdAsync(request.CompanyId, cancellationToken);
        if (ticketAdminUserId is null)
        {
            return Response<TicketDto>.Error(
                TicketInboundErrorCodes.NoTicketAdmin,
                ["No active TicketUserCompany with IsSuperAdminTicket is configured for the current tenant."]);
        }

        // --- 3. Load TicketCompanyDefault ---
        var defaultsRepository = _unitOfWork.GetRepository<TicketCompanyDefault>();
        var allDefaults = await defaultsRepository.GetAllAsync();
        var defaults = allDefaults.FirstOrDefault(x => x.CompanyId == request.CompanyId && x.GcRecord == BaseAuditableEntity.ActiveGcRecord);

        if (defaults is null
            || defaults.TicketStatusDefaultId is null
            || defaults.TicketComplexityDefaultId is null
            || defaults.TimeUnitDefaultId is null)
        {
            return Response<TicketDto>.Error(
                TicketInboundErrorCodes.DefaultsMissing,
                ["The tenant has no TicketCompanyDefault or its required fields are missing."]);
        }

        // --- 4. Resolve channel (for Name + FK) + requester person ---
        var channelRepository = _unitOfWork.GetRepository<CommunicationChannel>();
        var channel = await channelRepository.GetAsync(request.ChannelId);
        var channelName = channel?.Name ?? "External";

        var requesterPersonId = await ResolveRequesterPersonIdAsync(request, cancellationToken);

        // --- 5/6. Build the ticket entity ---
        var subject = Truncate(request.MessageSubject, 150, addEllipsis: true);
        if (string.IsNullOrWhiteSpace(subject))
        {
            subject = Truncate(request.MessageBody, 150, addEllipsis: true);
        }

        var descriptionBuilder = new System.Text.StringBuilder();
        if (requesterPersonId is null)
        {
            var display = string.IsNullOrWhiteSpace(request.SenderDisplayName) ? request.SenderIdentifier : request.SenderDisplayName;
            descriptionBuilder.Append("Remitente: ").Append(display).Append(" (").Append(request.SenderIdentifier).Append(")\n\n");
        }
        descriptionBuilder.Append(request.MessageBody);
        var description = descriptionBuilder.ToString();
        if (description.Length > 2000)
        {
            description = description[..2000];
        }

        var ticket = new Ticket
        {
            CompanyId = request.CompanyId,
            ChannelId = request.ChannelId,
            ExternalMessageId = request.ExternalMessageId,
            TicketStatusId = defaults.TicketStatusDefaultId!.Value,
            TicketComplexityId = defaults.TicketComplexityDefaultId!.Value,
            TimeUnitId = defaults.TimeUnitDefaultId!.Value,
            PersonId = requesterPersonId,
            AreaId = defaults.AreaDefaultId,
            ProjectId = defaults.ProjectDefaultId,
            CreatedByUserId = ticketAdminUserId.Value,
            AssignedToUserId = null,
            Name = subject,
            Description = description,
            IsVisibleToExternals = true,
            EstimatedTime = 0m,
            ConsumedTime = 0m
        };

        await _ticketCodeGenerator.AssignCodeAsync(ticket, DateTime.UtcNow, cancellationToken);

        ticket.AddLog(
            ticketAdminUserId.Value,
            LogType.Creation,
            $"Ticket creado automáticamente desde {channelName} ({DisplayOf(request)})");

        // --- 7. Persist + dedup-race guard ---
        var ticketRepository = _unitOfWork.GetRepository<Ticket>();
        try
        {
            await ticketRepository.InsertAsync(ticket);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (IsPostgresUniqueViolation(ex))
        {
            // Perdimos la carrera: alguien insertó con el mismo ExternalMessageId.
            // Re-buscamos y devolvemos el existente.
            var raced = await TryFindExistingTicket(request, cancellationToken);
            if (raced is null)
            {
                // Algo raro: el INSERT chocó pero el SELECT no encuentra nada.
                // Devolver el error original que el handler superior va a transformar a 500.
                throw;
            }

            return await BuildResponseAsync(raced, isDuplicate: true, cancellationToken);
        }

        // --- 8. Attachments (best-effort) ---
        if (request.Attachments.Count > 0)
        {
            await TryPersistAttachmentsAsync(ticket, request.Attachments, cancellationToken);
        }

        return await BuildResponseAsync(ticket, isDuplicate: false, cancellationToken);
    }

    /// <summary>
    /// Devuelve el ticket existente si hay un match activo con el mismo
    /// <c>(CompanyId, ChannelId, ExternalMessageId)</c>; <c>null</c> cuando no.
    /// </summary>
    private async Task<Ticket?> TryFindExistingTicket(RegisterInboundTicketCommand request, CancellationToken cancellationToken)
    {
        var ticketRepository = _unitOfWork.GetRepository<Ticket>();
        var all = await ticketRepository.GetAllIncludingDeletedAsync(t =>
            t.CompanyId == request.CompanyId
            && t.ChannelId == request.ChannelId
            && t.ExternalMessageId == request.ExternalMessageId
            && t.GcRecord == BaseAuditableEntity.ActiveGcRecord);

        return all.FirstOrDefault();
    }

    /// <summary>
    /// Resuelve el usuario "admin de tickets" de la empresa: <c>IsSuperAdminTicket = true</c> activo.
    /// </summary>
    private async Task<Guid?> ResolveSuperAdminUserIdAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.GetRepository<TicketUserCompany>();
        var rows = await repository.GetAllAsync();
        var row = rows.FirstOrDefault(x =>
            x.CompanyId == companyId
            && x.GcRecord == BaseAuditableEntity.ActiveGcRecord
            && x.IsSuperAdminTicket);

        if (row is null)
        {
            return null;
        }

        var userRepository = _unitOfWork.GetRepository<ApplicationUser>();
        var user = await userRepository.GetAsync(row.UserId);
        return user is not null ? user.Id : (Guid?)null;
    }

    /// <summary>
    /// Busca un <c>Person</c> cuyo <c>PersonContact.ContactValue</c> activo empate exacto con el
    /// <c>SenderIdentifier</c>. Coincidencia case-insensitive para emails y exacta para teléfonos.
    /// </summary>
    private async Task<Guid?> ResolveRequesterPersonIdAsync(RegisterInboundTicketCommand request, CancellationToken cancellationToken)
    {
        var contactsRepository = _unitOfWork.GetRepository<PersonContact>();
        var contacts = await contactsRepository.GetAllAsync();

        var identifier = request.SenderIdentifier?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(identifier))
        {
            return null;
        }

        var candidate = contacts.FirstOrDefault(c =>
            c.CompanyId == request.CompanyId
            && c.GcRecord == BaseAuditableEntity.ActiveGcRecord
            && c.IsActive
            && MatchesIdentifier(c, identifier));

        return candidate?.PersonId;
    }

    /// <summary>
    /// Match por tipo de contacto: emails son case-insensitive, teléfonos son exactos (E.164).
    /// </summary>
    private static bool MatchesIdentifier(PersonContact contact, string identifier)
    {
        if (string.IsNullOrWhiteSpace(contact.ContactValue))
        {
            return false;
        }

        return contact.ContactType switch
        {
            ContactType.PrimaryEmail => string.Equals(contact.ContactValue.Trim(), identifier, StringComparison.OrdinalIgnoreCase),
            ContactType.AlternativeEmail => string.Equals(contact.ContactValue.Trim(), identifier, StringComparison.OrdinalIgnoreCase),
            ContactType.MobilePhone => string.Equals(contact.ContactValue.Trim(), identifier, StringComparison.Ordinal),
            ContactType.WhatsApp => string.Equals(contact.ContactValue.Trim(), identifier, StringComparison.Ordinal),
            ContactType.Landline => string.Equals(contact.ContactValue.Trim(), identifier, StringComparison.Ordinal),
            _ => false
        };
    }

    /// <summary>
    /// Recorre cada <see cref="InboundAttachment"/>, aplica las reglas de
    /// <c>TicketAttachmentSettings</c> (mejor esfuerzo) y persiste los bytes /
    /// metadata. Nunca aborta la creación del ticket.
    /// </summary>
    private async Task TryPersistAttachmentsAsync(Ticket ticket, IReadOnlyList<InboundAttachment> attachments, CancellationToken cancellationToken)
    {
        var settingsRepository = _unitOfWork.GetRepository<JOIN.Domain.Messaging.TicketAttachmentSettings>();
        var allSettings = await settingsRepository.GetAllAsync();
        var settings = allSettings.FirstOrDefault(x => x.CompanyId == ticket.CompanyId && x.GcRecord == BaseAuditableEntity.ActiveGcRecord);

        if (settings is null)
        {
            _logger.LogWarning(
                "[RegisterInboundTicket] Skipping {AttachmentCount} attachments: no TicketAttachmentSettings configured for company {CompanyId}.",
                attachments.Count, ticket.CompanyId);
            return;
        }

        // El log Creation persiste en ticket.TicketLogs tras el InsertAsync; usamos el primero para anclar.
        var ticketLog = ticket.TicketLogs.FirstOrDefault();

        var documentRepository = _unitOfWork.GetRepository<TicketDocument>();
        var documentsPersisted = 0;
        var documentsSkipped = 0;

        foreach (var attachment in attachments)
        {
            if (attachment.Length > settings.MaxFileSizeBytes)
            {
                _logger.LogWarning(
                    "[RegisterInboundTicket] Skipping attachment {FileName}: {Size} bytes > MaxFileSizeBytes {Max}.",
                    attachment.FileName, attachment.Length, settings.MaxFileSizeBytes);
                documentsSkipped++;
                continue;
            }

            var extension = Path.GetExtension(attachment.FileName)?.ToLowerInvariant() ?? string.Empty;
            if (!TryMapExtension(extension, out var documentType))
            {
                _logger.LogWarning(
                    "[RegisterInboundTicket] Skipping attachment {FileName}: unsupported extension '{Extension}'.",
                    attachment.FileName, extension);
                documentsSkipped++;
                continue;
            }

            if (!settings.AllowedDocumentTypes.HasFlag(documentType))
            {
                _logger.LogWarning(
                    "[RegisterInboundTicket] Skipping attachment {FileName}: document type '{DocumentType}' not allowed for company {CompanyId}.",
                    attachment.FileName, documentType, ticket.CompanyId);
                documentsSkipped++;
                continue;
            }

            try
            {
                var newName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
                var storageKey = $"{ticket.CompanyId}/{ticket.Id}/{newName}";
                var saveResult = await _fileStorageService.SaveAsync(
                    attachment.Content,
                    storageKey,
                    attachment.ContentType,
                    cancellationToken);

                await documentRepository.InsertAsync(new TicketDocument
                {
                    TicketId = ticket.Id,
                    CompanyId = ticket.CompanyId,
                    TicketLogsId = ticketLog?.Id ?? Guid.Empty,
                    DocumentType = documentType,
                    OriginalName = attachment.FileName,
                    NewName = newName,
                    Path = storageKey,
                    StorageProvider = StorageProviderKind.Local,
                    ContentType = attachment.ContentType,
                    SizeBytes = saveResult.SizeBytes,
                    Created = DateTime.UtcNow,
                    CreatedBy = ticket.CreatedByUserId.ToString(),
                    GcRecord = BaseAuditableEntity.ActiveGcRecord
                });
                documentsPersisted++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[RegisterInboundTicket] Skipping attachment {FileName}: storage provider failed.",
                    attachment.FileName);
                documentsSkipped++;
            }
        }

        if (documentsPersisted > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (documentsSkipped > 0)
        {
            _logger.LogInformation(
                "[RegisterInboundTicket] Ticket {TicketId}: persisted {Persisted} attachment(s), skipped {Skipped}.",
                ticket.Id, documentsPersisted, documentsSkipped);
        }
    }

    /// <summary>
    /// Cierra el flujo devolviendo el <see cref="TicketDto"/> correspondiente.
    /// </summary>
    private async Task<Response<TicketDto>> BuildResponseAsync(Ticket ticket, bool isDuplicate, CancellationToken cancellationToken)
    {
        var companyRepository = _unitOfWork.GetRepository<Company>();
        var company = await companyRepository.GetAsync(ticket.CompanyId);

        var dto = await _ticketDtoAssembler.BuildAsync(ticket, company!, cancellationToken);
        return new Response<TicketDto>
        {
            IsSuccess = true,
            Message = isDuplicate ? TicketInboundErrorCodes.Duplicate : "Ticket registered successfully.",
            Data = dto
        };
    }

    private static string Truncate(string? value, int maxLength, bool addEllipsis)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        return addEllipsis ? value[..(maxLength - 1)] + "…" : value[..maxLength];
    }

    private static string DisplayOf(RegisterInboundTicketCommand request) =>
        string.IsNullOrWhiteSpace(request.SenderDisplayName)
            ? request.SenderIdentifier
            : $"{request.SenderDisplayName} ({request.SenderIdentifier})";

    /// <summary>
    /// Detecta la violación de índice único de PostgreSQL (SQLSTATE 23505) sin
    /// tomar dependencia dura del paquete Npgsql — mismo criterio que
    /// <c>GlobalExceptionHandler.IsPostgresUniqueViolation</c>.
    /// </summary>
    private static bool IsPostgresUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().FullName == "Npgsql.PostgresException")
            {
                var sqlState = current.GetType().GetProperty("SqlState")?.GetValue(current) as string;
                return sqlState == "23505";
            }
        }

        return false;
    }

    /// <summary>
    /// Mapea la extensión a <see cref="DocumentType"/> — mismo criterio que
    /// <c>UploadTicketDocumentCommandHandler.TryMapExtension</c> (SPEC 36).
    /// </summary>
    private static bool TryMapExtension(string extension, out DocumentType documentType)
    {
        documentType = DocumentType.None;
        return extension switch
        {
            ".pdf" => Assign(DocumentType.Pdf, out documentType),
            ".doc" or ".docx" => Assign(DocumentType.Word, out documentType),
            ".xls" or ".xlsx" => Assign(DocumentType.Excel, out documentType),
            ".txt" or ".csv" => Assign(DocumentType.Text, out documentType),
            ".png" or ".jpg" or ".jpeg" or ".gif" => Assign(DocumentType.Image, out documentType),
            ".zip" => Assign(DocumentType.Other, out documentType),
            _ => false
        };

        static bool Assign(DocumentType value, out DocumentType slot)
        {
            slot = value;
            return true;
        }
    }
}
