using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.UploadTicketDocument;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Security;
using MediatR;
using System.Text.Json.Serialization;

namespace JOIN.Application.UseCases.Customers.Leads.Documents;

/// <summary>
/// <c>POST /leads/{leadId}/documents</c> (SPEC 62): attaches a file to an open lead and, optionally, to one of its
/// contacts. The document type is inferred from the extension (same table as ticket documents).
/// </summary>
public sealed record UploadLeadDocumentCommand : ITransactionalCommand<Response<LeadDocumentDto>>
{
    [JsonIgnore]
    public Guid LeadId { get; init; }
    public Guid? LeadInteractionId { get; init; }
    public InboundAttachment File { get; init; } = null!;
}

/// <summary><c>DELETE /leads/{leadId}/documents/{id}</c>: logical delete; the file is kept.</summary>
public sealed record DeleteLeadDocumentCommand(Guid LeadId, Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>POST /leads/{leadId}/documents/{id}/restore</c> (SPEC 41-post, SuperAdmin).</summary>
public sealed record RestoreLeadDocumentCommand(Guid LeadId, Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;

/// <summary>Validates <see cref="UploadLeadDocumentCommand"/>.</summary>
public sealed class UploadLeadDocumentCommandValidator : AbstractValidator<UploadLeadDocumentCommand>
{
    public UploadLeadDocumentCommandValidator()
    {
        RuleFor(x => x.LeadId).NotEmpty();
        RuleFor(x => x.File).NotNull().WithMessage("File is required.");
        RuleFor(x => x.File!.FileName).NotEmpty().MaximumLength(150).When(x => x.File != null);
        RuleFor(x => x.File!.ContentType).NotEmpty().MaximumLength(150).When(x => x.File != null);
        RuleFor(x => x.File!.Length).GreaterThan(0).When(x => x.File != null);
    }
}

/// <summary>Handles <see cref="UploadLeadDocumentCommand"/>.</summary>
public sealed class UploadLeadDocumentCommandHandler(
    IUnitOfWork unitOfWork,
    LeadCommandContext context,
    IFileStorageResolver fileStorageResolver)
    : IRequestHandler<UploadLeadDocumentCommand, Response<LeadDocumentDto>>
{
    public async Task<Response<LeadDocumentDto>> Handle(UploadLeadDocumentCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<LeadDocumentDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (load, error) = await context.LoadAsync<LeadDocumentDto>(actor, request.LeadId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var lead = load!.Lead;
        if (LeadCommandContext.RequireOpen<LeadDocumentDto>(lead) is { } notOpen)
        {
            return notOpen;
        }

        if (request.LeadInteractionId is { } interactionId
            && !(await unitOfWork.GetRepository<LeadInteraction>().GetAllIncludingDeletedAsync(i => i.Id == interactionId && i.LeadId == lead.Id && i.GcRecord == 0)).Any())
        {
            return Response<LeadDocumentDto>.Error(LeadErrorCodes.InvalidLeadInteraction, ["The contact does not belong to the lead."]);
        }

        var settings = (await unitOfWork.GetRepository<LeadAttachmentSettings>().GetAllIncludingDeletedAsync(s => s.CompanyId == actor.CompanyId && s.GcRecord == 0))
            .FirstOrDefault();
        if (settings is null)
        {
            return Response<LeadDocumentDto>.Error(LeadErrorCodes.AttachmentSettingsNotConfigured, ["The company has no lead attachment settings."]);
        }

        var extension = Path.GetExtension(request.File.FileName)?.ToLowerInvariant() ?? string.Empty;
        if (!UploadTicketDocumentCommandHandler.TryMapExtension(extension, out var documentType) || !settings.AllowedDocumentTypes.HasFlag(documentType))
        {
            return Response<LeadDocumentDto>.Error(LeadErrorCodes.DocumentTypeNotAllowed, [$"The file type '{extension}' is not allowed for leads in the company."]);
        }

        if (request.File.Length > settings.MaxFileSizeBytes)
        {
            return Response<LeadDocumentDto>.Error(LeadErrorCodes.DocumentTooLarge, [$"File size {request.File.Length} bytes exceeds the maximum allowed ({settings.MaxFileSizeBytes} bytes)."]);
        }

        var documents = unitOfWork.GetRepository<LeadDocument>();
        if (request.LeadInteractionId is { } contactId)
        {
            var perInteraction = (await documents.GetAllIncludingDeletedAsync(d => d.LeadInteractionId == contactId && d.GcRecord == 0)).Count();
            if (perInteraction >= settings.MaxFilesPerInteraction)
            {
                return Response<LeadDocumentDto>.Error(LeadErrorCodes.DocumentLimitPerInteraction, [$"The contact already has {perInteraction} documents (max {settings.MaxFilesPerInteraction})."]);
            }
        }

        if (settings.MaxFilesPerDay is { } perDayLimit)
        {
            var todayUtc = DateTime.UtcNow.Date;
            var today = (await documents.GetAllIncludingDeletedAsync(d => d.CompanyId == actor.CompanyId && d.GcRecord == 0 && d.Created >= todayUtc)).Count();
            if (today >= perDayLimit)
            {
                return Response<LeadDocumentDto>.Error(LeadErrorCodes.DocumentLimitPerDay, [$"The company reached the daily quota of {perDayLimit} lead documents."]);
            }
        }

        var newName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        var storageKey = $"leads/{actor.CompanyId}/{lead.Id}/{newName}";
        var provider = fileStorageResolver.Current;
        var saved = await provider.SaveAsync(request.File.Content, storageKey, request.File.ContentType, cancellationToken);

        var entity = new LeadDocument
        {
            CompanyId = actor.CompanyId,
            LeadId = lead.Id,
            LeadInteractionId = request.LeadInteractionId,
            DocumentType = documentType,
            OriginalName = request.File.FileName,
            NewName = newName,
            Path = storageKey,
            StorageProvider = provider.Kind,
            ContentType = request.File.ContentType,
            SizeBytes = saved.SizeBytes,
            UploadedByUserId = actor.UserId
        };
        await documents.InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var user = (await unitOfWork.GetRepository<ApplicationUser>().GetAllIncludingDeletedAsync(u => u.Id == actor.UserId)).FirstOrDefault();
        return new Response<LeadDocumentDto>
        {
            IsSuccess = true,
            Message = "Lead document uploaded successfully.",
            Data = new LeadDocumentDto
            {
                Id = entity.Id,
                LeadId = entity.LeadId,
                LeadInteractionId = entity.LeadInteractionId,
                DocumentType = entity.DocumentType.ToString(),
                OriginalName = entity.OriginalName,
                ContentType = entity.ContentType,
                SizeBytes = entity.SizeBytes,
                UploadedByUserId = actor.UserId,
                UploadedByUserName = user is null ? null : $"{user.FirstName} {user.LastName}".Trim(),
                CreatedAt = entity.Created
            }
        };
    }
}

/// <summary>Handles <see cref="DeleteLeadDocumentCommand"/>.</summary>
public sealed class DeleteLeadDocumentCommandHandler(IUnitOfWork unitOfWork, LeadCommandContext context)
    : IRequestHandler<DeleteLeadDocumentCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteLeadDocumentCommand request, CancellationToken cancellationToken)
    {
        if (context.Actor<Guid>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (_, error) = await context.LoadAsync<Guid>(actor, request.LeadId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var repository = unitOfWork.GetRepository<LeadDocument>();
        var document = await repository.GetAsync(request.Id);
        if (document is null || document.LeadId != request.LeadId || document.CompanyId != actor.CompanyId || document.GcRecord != 0)
        {
            return Response<Guid>.Error(LeadErrorCodes.DocumentNotFound, ["Lead document not found."]);
        }

        document.MarkAsDeleted();
        await repository.UpdateAsync(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<Guid> { IsSuccess = true, Message = "Lead document deleted successfully.", Data = document.Id };
    }
}

/// <summary>Handles <see cref="RestoreLeadDocumentCommand"/> with <see cref="SoftDeleteRestorer"/> (SPEC 41-post).</summary>
public sealed class RestoreLeadDocumentCommandHandler(ICurrentUserService currentUserService, SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreLeadDocumentCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreLeadDocumentCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(LeadErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(LeadErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<LeadDocument>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (document, _) => document.LeadId != request.LeadId || await restorer.IsParentDeletedAsync<Lead>(document.LeadId),
            cancellationToken: cancellationToken);
    }
}
