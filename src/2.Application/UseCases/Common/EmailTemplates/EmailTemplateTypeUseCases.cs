using Dapper;
using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Email;
using MediatR;
using Settings = JOIN.Domain.Admin.EmailTemplateType;

namespace JOIN.Application.UseCases.Common.EmailTemplates;

/// <summary>Fields shared by the create and update commands of an email template type.</summary>
public interface IEmailTemplateTypeInput
{
    string Code { get; }
    string Name { get; }
    int SortOrder { get; }
    bool IsActive { get; }
}

/// <summary><c>POST /EmailTemplateTypes</c> (SPEC 71): adds a classification to the company's catalog.</summary>
public sealed record CreateEmailTemplateTypeCommand : IEmailTemplateTypeInput, ITransactionalCommand<Response<EmailTemplateTypeDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary><c>PUT /EmailTemplateTypes/{id}</c> (SPEC 71).</summary>
public sealed record UpdateEmailTemplateTypeCommand : IEmailTemplateTypeInput, ITransactionalCommand<Response<EmailTemplateTypeDto>>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>SPEC 71 — <c>DELETE /EmailTemplateTypes/{id}</c>: blocked by <c>EMAIL_TEMPLATE_TYPE_IN_USE</c> when there are live templates.</summary>
public sealed record DeleteEmailTemplateTypeCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary>SPEC 71 — <c>POST /EmailTemplateTypes/{id}/restore</c>: SuperAdmin only.</summary>
public sealed record RestoreEmailTemplateTypeCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>GET /EmailTemplateTypes</c>: the company's live catalog (optional inactive rows).</summary>
public sealed record GetEmailTemplateTypesQuery(bool? IncludeInactive = null) : IRequest<Response<IReadOnlyList<EmailTemplateTypeDto>>>;

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

public sealed class CreateEmailTemplateTypeCommandValidator : AbstractValidator<CreateEmailTemplateTypeCommand>
{
    public CreateEmailTemplateTypeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Settings.MaxCodeLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Settings.MaxNameLength);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateEmailTemplateTypeCommandValidator : AbstractValidator<UpdateEmailTemplateTypeCommand>
{
    public UpdateEmailTemplateTypeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Settings.MaxCodeLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Settings.MaxNameLength);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

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

/// <summary>Handles <see cref="GetEmailTemplateTypesQuery"/> with Dapper.</summary>
public sealed class GetEmailTemplateTypesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetEmailTemplateTypesQuery, Response<IReadOnlyList<EmailTemplateTypeDto>>>
{
    public async Task<Response<IReadOnlyList<EmailTemplateTypeDto>>> Handle(GetEmailTemplateTypesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateTypeErrors.CompanyRequired<IReadOnlyList<EmailTemplateTypeDto>>();
        }

        const string sql = """
            SELECT Id, CompanyId, Code, Name, SortOrder, IsActive, Created AS CreatedAt
            FROM admin.emailtemplatetypes
            WHERE CompanyId = @CompanyId
              AND GcRecord = 0
              AND (@IncludeInactive = TRUE OR IsActive = TRUE)
            ORDER BY SortOrder, Name;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<EmailTemplateTypeDto>(new CommandDefinition(
            sql,
            new { CompanyId = currentUserService.CompanyId, IncludeInactive = request.IncludeInactive == true },
            cancellationToken: cancellationToken));

        return new Response<IReadOnlyList<EmailTemplateTypeDto>>
        {
            IsSuccess = true,
            Message = "Email template types retrieved successfully.",
            Data = rows.AsList()
        };
    }
}
