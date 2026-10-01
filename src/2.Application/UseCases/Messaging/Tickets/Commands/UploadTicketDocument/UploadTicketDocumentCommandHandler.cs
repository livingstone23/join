using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Domain.Enums;
using JOIN.Domain.Support;
using MediatR;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.UploadTicketDocument;

/// <summary>
/// Maneja la subida de un documento adjunto a un ticket. Aplica las reglas
/// de la spec sección "UploadTicketDocumentCommandHandler — flujo completo":
/// tenant + ticket + log + settings + tipo + tamaño + cupos. El
/// <c>DocumentType</c> se infiere server-side de la extensión — el cliente
/// nunca lo envía.
/// </summary>
public sealed class UploadTicketDocumentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IFileStorageService fileStorageService)
    : IRequestHandler<UploadTicketDocumentCommand, Response<TicketDocumentDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IFileStorageService _fileStorageService = fileStorageService;

    public async Task<Response<TicketDocumentDto>> Handle(UploadTicketDocumentCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<TicketDocumentDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(_currentUserService.UserId, out var actorUserId))
        {
            return Response<TicketDocumentDto>.Error("USER_REQUIRED", ["The authenticated user identifier is required."]);
        }

        // --- 3. Load Ticket ---
        var ticketRepository = _unitOfWork.GetRepository<JOIN.Domain.Messaging.Ticket>();
        var ticket = await ticketRepository.GetAsync(request.TicketId);
        if (ticket is null || ticket.CompanyId != tenantId)
        {
            return Response<TicketDocumentDto>.Error("TICKET_NOT_FOUND", ["Ticket not found for the current tenant."]);
        }

        // --- 4. Resolve TicketLogsId (explicit or latest active) ---
        var ticketLogRepository = _unitOfWork.GetRepository<JOIN.Domain.Support.TicketLog>();
        JOIN.Domain.Support.TicketLog? ticketLog;
        if (request.TicketLogsId.HasValue)
        {
            var explicitLog = await ticketLogRepository.GetAsync(request.TicketLogsId.Value);
            if (explicitLog is null || explicitLog.TicketId != request.TicketId || explicitLog.CompanyId != tenantId)
            {
                return Response<TicketDocumentDto>.Error("INVALID_TICKET_LOG", ["The supplied ticket log does not belong to the ticket in the current tenant."]);
            }
            ticketLog = explicitLog;
        }
        else
        {
            var allLogs = await ticketLogRepository.GetAllAsync();
            ticketLog = allLogs
                .Where(x => x.GcRecord == 0 && x.TicketId == request.TicketId && x.CompanyId == tenantId)
                .OrderByDescending(x => x.Created)
                .FirstOrDefault();
            if (ticketLog is null)
            {
                return Response<TicketDocumentDto>.Error("INVALID_TICKET_LOG", ["The ticket has no active log to anchor the document."]);
            }
        }

        // --- 5. Load TicketAttachmentSettings (singleton per tenant) ---
        var settingsRepository = _unitOfWork.GetRepository<TicketAttachmentSettingsEntity>();
        var allSettings = await settingsRepository.GetAllAsync();
        var settings = allSettings.FirstOrDefault(x => x.GcRecord == 0 && x.CompanyId == tenantId);
        if (settings is null)
        {
            return Response<TicketDocumentDto>.Error("ATTACHMENT_SETTINGS_NOT_CONFIGURED", ["The tenant has no ticket attachment settings configured."]);
        }

        // --- 6. Infer DocumentType from extension ---
        var extension = Path.GetExtension(request.File.FileName)?.ToLowerInvariant() ?? string.Empty;
        if (!TryMapExtension(extension, out var documentType))
        {
            return Response<TicketDocumentDto>.Error("UNSUPPORTED_DOCUMENT_TYPE", [$"Extension '{extension}' is not supported."]);
        }

        // --- 7. Allowed by company whitelist ---
        if (!settings.AllowedDocumentTypes.HasFlag(documentType))
        {
            return Response<TicketDocumentDto>.Error("DOCUMENT_TYPE_NOT_ALLOWED", [$"The document type '{documentType}' is not allowed for the current tenant."]);
        }

        // --- 8. Max file size ---
        if (request.File.Length > settings.MaxFileSizeBytes)
        {
            return Response<TicketDocumentDto>.Error("FILE_TOO_LARGE", [$"File size {request.File.Length} bytes exceeds the maximum allowed ({settings.MaxFileSizeBytes} bytes)."]);
        }

        // --- 9. Max files per ticket ---
        var documentRepository = _unitOfWork.GetRepository<TicketDocument>();
        var allDocuments = await documentRepository.GetAllAsync();
        var activeTicketDocs = allDocuments
            .Where(x => x.GcRecord == 0 && x.TicketId == request.TicketId)
            .Count();

        if (activeTicketDocs >= settings.MaxFilesPerTicket)
        {
            return Response<TicketDocumentDto>.Error("MAX_FILES_PER_TICKET_REACHED", [$"The ticket already has {activeTicketDocs} attachments (max {settings.MaxFilesPerTicket})."]);
        }

        // --- 10. Daily quota (only when configured) ---
        if (settings.MaxFilesPerDay.HasValue)
        {
            var todayUtc = DateTime.UtcNow.Date;
            var todayCount = allDocuments
                .Count(x => x.GcRecord == 0 && x.CompanyId == tenantId && x.Created >= todayUtc);
            if (todayCount >= settings.MaxFilesPerDay.Value)
            {
                return Response<TicketDocumentDto>.Error("DAILY_ATTACHMENT_QUOTA_REACHED", [$"The tenant has reached the daily quota of {settings.MaxFilesPerDay.Value} attachments."]);
            }
        }

        // --- 11/12. Persist file under deterministic storageKey ---
        var newName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        var storageKey = $"{tenantId}/{request.TicketId}/{newName}";

        var saveResult = await _fileStorageService.SaveAsync(
            request.File.Content,
            storageKey,
            request.File.ContentType,
            cancellationToken);

        // --- 13. Build entity ---
        var entity = new TicketDocument
        {
            TicketId = request.TicketId,
            TicketLogsId = ticketLog.Id,
            DocumentType = documentType,
            OriginalName = request.File.FileName,
            NewName = newName,
            Path = storageKey,
            StorageProvider = StorageProviderKind.Local,
            ContentType = request.File.ContentType,
            SizeBytes = saveResult.SizeBytes
        };

        // --- 14. Persist metadata ---
        await documentRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<TicketDocumentDto>.Error("UPLOAD_FAILED", ["No records were affected while saving the document metadata."]);
        }

        var createdByName = await ResolveUserNameAsync(actorUserId, cancellationToken);

        return new Response<TicketDocumentDto>
        {
            IsSuccess = true,
            Message = "Ticket document uploaded successfully.",
            Data = BuildDto(entity, createdByName)
        };
    }

    private async Task<string?> ResolveUserNameAsync(Guid userId, CancellationToken cancellationToken)
    {
        var userRepository = _unitOfWork.GetRepository<Domain.Security.ApplicationUser>();
        var user = await userRepository.GetAsync(userId);
        if (user is null)
        {
            return null;
        }
        return string.Join(" ", new[] { user.FirstName, user.LastName }
            .Where(v => !string.IsNullOrWhiteSpace(v)));
    }

    private static TicketDocumentDto BuildDto(JOIN.Domain.Support.TicketDocument entity, string? createdByName) => new()
    {
        Id = entity.Id,
        TicketId = entity.TicketId,
        TicketLogsId = entity.TicketLogsId,
        DocumentType = entity.DocumentType.ToString(),
        OriginalName = entity.OriginalName,
        ContentType = entity.ContentType,
        SizeBytes = entity.SizeBytes,
        CreatedByUserName = createdByName,
        CreatedAt = entity.Created
    };

    /// <summary>
    /// Mapea la extensión del archivo al <see cref="DocumentType"/> según la
    /// tabla del spec sección "Data model". Una extensión no reconocida
    /// devuelve <c>false</c> y el handler emite <c>UNSUPPORTED_DOCUMENT_TYPE</c>.
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