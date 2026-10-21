using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Email;
using MediatR;
using Settings = JOIN.Domain.Admin.EmailTemplateType;

namespace JOIN.Application.UseCases.Common.EmailTemplateTypes.Commands;

/// <summary>Handles <see cref="CreateEmailTemplateTypeCommand"/>.</summary>
public sealed class CreateEmailTemplateTypeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateEmailTemplateTypeCommand, Response<EmailTemplateTypeDto>>
{
    public async Task<Response<EmailTemplateTypeDto>> Handle(CreateEmailTemplateTypeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateTypeErrors.CompanyRequired<EmailTemplateTypeDto>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Settings>();

        var normalizedCode = Settings.NormalizeCode(request.Code);
        var duplicate = (await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.Code == normalizedCode && x.GcRecord == 0)).Any();
        if (duplicate)
        {
            return EmailTemplateTypeErrors.DuplicateCode<EmailTemplateTypeDto>();
        }

        var entity = new Settings { CompanyId = companyId };
        EmailTemplateTypeRules.Apply(entity, request);
        await repository.InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<EmailTemplateTypeDto>
        {
            IsSuccess = true,
            Message = "Email template type created successfully.",
            Data = EmailTemplateTypeRules.ToDto(entity)
        };
    }
}

/// <summary>Handles <see cref="UpdateEmailTemplateTypeCommand"/>.</summary>
public sealed class UpdateEmailTemplateTypeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateEmailTemplateTypeCommand, Response<EmailTemplateTypeDto>>
{
    public async Task<Response<EmailTemplateTypeDto>> Handle(UpdateEmailTemplateTypeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateTypeErrors.CompanyRequired<EmailTemplateTypeDto>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Settings>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return EmailTemplateTypeErrors.NotFound<EmailTemplateTypeDto>();
        }

        var normalizedCode = Settings.NormalizeCode(request.Code);
        var duplicate = (await repository.GetAllIncludingDeletedAsync(
            x => x.CompanyId == companyId
                && x.Id != request.Id
                && x.Code == normalizedCode
                && x.GcRecord == 0)).Any();
        if (duplicate)
        {
            return EmailTemplateTypeErrors.DuplicateCode<EmailTemplateTypeDto>();
        }

        EmailTemplateTypeRules.Apply(entity, request);
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<EmailTemplateTypeDto>
        {
            IsSuccess = true,
            Message = "Email template type updated successfully.",
            Data = EmailTemplateTypeRules.ToDto(entity)
        };
    }
}

/// <summary>Handles <see cref="DeleteEmailTemplateTypeCommand"/> — blocks when live templates reference the type.</summary>
public sealed class DeleteEmailTemplateTypeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteEmailTemplateTypeCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteEmailTemplateTypeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateTypeErrors.CompanyRequired<Guid>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Settings>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return EmailTemplateTypeErrors.NotFound<Guid>();
        }

        var check = await new ActiveDependentsCheck(unitOfWork).CountAsync<JOIN.Domain.Admin.EmailTemplate>(
            x => x.GcRecord == 0 && x.EmailTemplateTypeId == entity.Id,
            "EmailTemplates");
        if (check.HasDependents)
        {
            return Response<Guid>.Error(EmailTemplateErrorCodes.EmailTemplateTypeInUse, [.. check.Details]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid> { IsSuccess = true, Message = "Email template type deleted successfully.", Data = entity.Id };
    }
}

/// <summary>Handles <see cref="RestoreEmailTemplateTypeCommand"/> with <see cref="SoftDeleteRestorer"/>.</summary>
public sealed class RestoreEmailTemplateTypeCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreEmailTemplateTypeCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreEmailTemplateTypeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateTypeErrors.CompanyRequired<Guid>();
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(EmailTemplateErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<Settings>(
            request.Id,
            request.CompanyId,
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<Settings>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.Code == entity.Code),
            cancellationToken: cancellationToken);
    }
}

internal static class EmailTemplateTypeRules
{
    internal static void Apply(Settings entity, IEmailTemplateTypeInput input)
    {
        entity.Code = Settings.NormalizeCode(input.Code);
        entity.Name = input.Name.Trim();
        entity.SortOrder = input.SortOrder;
        entity.IsActive = input.IsActive;
    }

    internal static EmailTemplateTypeDto ToDto(Settings t) => new()
    {
        Id = t.Id,
        CompanyId = t.CompanyId,
        Code = t.Code,
        Name = t.Name,
        SortOrder = t.SortOrder,
        IsActive = t.IsActive,
        CreatedAt = t.Created
    };
}

internal static class EmailTemplateTypeErrors
{
    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(EmailTemplateErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    internal static Response<T> NotFound<T>() =>
        Response<T>.Error(EmailTemplateErrorCodes.EmailTemplateTypeNotFound, ["Email template type not found."]);

    internal static Response<T> DuplicateCode<T>() =>
        Response<T>.Error(EmailTemplateErrorCodes.DuplicateKey, ["An email template type with that code already exists in this company."]);
}
