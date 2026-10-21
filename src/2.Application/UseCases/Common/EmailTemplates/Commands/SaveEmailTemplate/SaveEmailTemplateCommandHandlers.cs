using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Email;
using MediatR;
using Template = JOIN.Domain.Admin.EmailTemplate;
using TemplateType = JOIN.Domain.Admin.EmailTemplateType;

namespace JOIN.Application.UseCases.Common.EmailTemplates.Commands;

/// <summary>Handles <see cref="CreateEmailTemplateCommand"/>.</summary>
public sealed class CreateEmailTemplateCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    EmailTemplateWriteGuard guard)
    : IRequestHandler<CreateEmailTemplateCommand, Response<EmailTemplateDto>>
{
    public async Task<Response<EmailTemplateDto>> Handle(CreateEmailTemplateCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateErrors.CompanyRequired<EmailTemplateDto>();
        }

        var companyId = currentUserService.CompanyId;

        var (type, typeError) = await guard.ResolveTypeAsync(companyId, request.EmailTemplateTypeId, cancellationToken);
        if (typeError is not null)
        {
            return Response<EmailTemplateDto>.Error(typeError.Message, typeError.Errors);
        }

        var validationError = await guard.ValidateAsync(
            companyId, null, request.Name, request.Subject, request.BodyHtml, cancellationToken);
        if (validationError is not null)
        {
            return Response<EmailTemplateDto>.Error(validationError.Message, validationError.Errors);
        }

        var entity = new Template { CompanyId = companyId };
        EmailTemplateRules.Apply(entity, request);
        entity.BodyHtml = guard.Sanitize(entity.BodyHtml);

        await unitOfWork.GetRepository<Template>().InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<EmailTemplateDto>
        {
            IsSuccess = true,
            Message = "Email template created successfully.",
            Data = EmailTemplateRules.ToDto(entity, type)
        };
    }
}

/// <summary>Handles <see cref="UpdateEmailTemplateCommand"/>.</summary>
public sealed class UpdateEmailTemplateCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    EmailTemplateWriteGuard guard)
    : IRequestHandler<UpdateEmailTemplateCommand, Response<EmailTemplateDto>>
{
    public async Task<Response<EmailTemplateDto>> Handle(UpdateEmailTemplateCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateErrors.CompanyRequired<EmailTemplateDto>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Template>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return EmailTemplateErrors.NotFound<EmailTemplateDto>();
        }

        // An inactive type cannot be assigned, but a template keeps the type it already has (errors table).
        var (type, typeError) = await guard.ResolveTypeAsync(
            companyId, request.EmailTemplateTypeId, cancellationToken, allowInactive: request.EmailTemplateTypeId == entity.EmailTemplateTypeId);
        if (typeError is not null)
        {
            return Response<EmailTemplateDto>.Error(typeError.Message, typeError.Errors);
        }

        var validationError = await guard.ValidateAsync(
            companyId, entity.Id, request.Name, request.Subject, request.BodyHtml, cancellationToken);
        if (validationError is not null)
        {
            return Response<EmailTemplateDto>.Error(validationError.Message, validationError.Errors);
        }

        EmailTemplateRules.Apply(entity, request);
        entity.BodyHtml = guard.Sanitize(entity.BodyHtml);

        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<EmailTemplateDto>
        {
            IsSuccess = true,
            Message = "Email template updated successfully.",
            Data = EmailTemplateRules.ToDto(entity, type)
        };
    }
}

/// <summary>Handles <see cref="DeleteEmailTemplateCommand"/>: logical delete (SPEC 41-post).</summary>
public sealed class DeleteEmailTemplateCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteEmailTemplateCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteEmailTemplateCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateErrors.CompanyRequired<Guid>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Template>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return EmailTemplateErrors.NotFound<Guid>();
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid> { IsSuccess = true, Message = "Email template deleted successfully.", Data = entity.Id };
    }
}

/// <summary>
/// Handles <see cref="RestoreEmailTemplateCommand"/> with <see cref="SoftDeleteRestorer"/>: not while its type is
/// deleted (<c>PARENT_DELETED</c>) nor when a live template already uses the name (<c>ACTIVE_DUPLICATE_EXISTS</c>).
/// </summary>
public sealed class RestoreEmailTemplateCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreEmailTemplateCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreEmailTemplateCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateErrors.CompanyRequired<Guid>();
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(EmailTemplateErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<Template>(
            request.Id,
            request.CompanyId,
            isParentDeleted: (entity, _) => restorer.IsParentDeletedAsync<TemplateType>(entity.EmailTemplateTypeId),
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<Template>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.Name == entity.Name),
            cancellationToken: cancellationToken);
    }
}

internal static class EmailTemplateRules
{
    internal static void Apply(Template entity, IEmailTemplateInput input)
    {
        entity.EmailTemplateTypeId = input.EmailTemplateTypeId;
        entity.Name = input.Name.Trim();
        entity.Subject = input.Subject.Trim();
        entity.BodyHtml = input.BodyHtml;
        entity.IsActive = input.IsActive;
    }

    internal static EmailTemplateDto ToDto(Template t, TemplateType? type) => new()
    {
        Id = t.Id,
        CompanyId = t.CompanyId,
        EmailTemplateTypeId = t.EmailTemplateTypeId,
        EmailTemplateTypeName = type?.Name ?? string.Empty,
        EmailTemplateTypeCode = type?.Code ?? string.Empty,
        Name = t.Name,
        Subject = t.Subject,
        BodyHtml = t.BodyHtml,
        IsActive = t.IsActive,
        CreatedAt = t.Created
    };
}
