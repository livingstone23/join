using System.Text;
using Dapper;
using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Email;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Email;
using MediatR;
using Microsoft.Extensions.Options;
using Template = JOIN.Domain.Admin.EmailTemplate;
using TemplateType = JOIN.Domain.Admin.EmailTemplateType;

namespace JOIN.Application.UseCases.Common.EmailTemplates;

/// <summary>Fields shared by the create and update commands of an email template.</summary>
public interface IEmailTemplateInput
{
    Guid EmailTemplateTypeId { get; }
    string Name { get; }
    string Subject { get; }
    string BodyHtml { get; }
    bool IsActive { get; }
}

/// <summary><c>POST /EmailTemplates</c> (SPEC 71): a template of the company, grouped by type.</summary>
public sealed record CreateEmailTemplateCommand : IEmailTemplateInput, ITransactionalCommand<Response<EmailTemplateDto>>
{
    public Guid EmailTemplateTypeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
}

/// <summary><c>PUT /EmailTemplates/{id}</c> (SPEC 71).</summary>
public sealed record UpdateEmailTemplateCommand : IEmailTemplateInput, ITransactionalCommand<Response<EmailTemplateDto>>
{
    public Guid Id { get; init; }
    public Guid EmailTemplateTypeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
}

/// <summary>SPEC 71 — <c>DELETE /EmailTemplates/{id}</c>: logical delete, the file is not involved (there is none).</summary>
public sealed record DeleteEmailTemplateCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary>SPEC 71 — <c>POST /EmailTemplates/{id}/restore</c>: SuperAdmin only.</summary>
public sealed record RestoreEmailTemplateCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>GET /EmailTemplates</c>: the company's templates, paged and filtered.</summary>
public sealed record GetEmailTemplatesQuery(
    string? Search = null,
    Guid? EmailTemplateTypeId = null,
    bool? IsActive = null,
    int PageNumber = 1,
    int PageSize = 20) : IRequest<Response<PagedResult<EmailTemplateDto>>>;

/// <summary><c>GET /EmailTemplates/{id}</c>: the detail of one template.</summary>
public sealed record GetEmailTemplateByIdQuery(Guid Id) : IRequest<Response<EmailTemplateDto>>;

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

internal static class EmailTemplateErrors
{
    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(EmailTemplateErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    internal static Response<T> NotFound<T>() =>
        Response<T>.Error(EmailTemplateErrorCodes.EmailTemplateNotFound, ["Email template not found."]);

    internal static Response<T> TypeNotFound<T>() =>
        Response<T>.Error(EmailTemplateErrorCodes.EmailTemplateTypeNotFound, ["Email template type not found or inactive."]);

    internal static Response<T> DuplicateName<T>() =>
        Response<T>.Error(EmailTemplateErrorCodes.DuplicateKey, ["An email template with that name already exists in this company."]);

    internal static Response<T> UnknownVariables<T>(IReadOnlyList<string> variables) =>
        Response<T>.Error(
            EmailTemplateErrorCodes.EmailTemplateUnknownVariable,
            [$"Unknown template variables: {string.Join(", ", variables)}."]);
}

public sealed class CreateEmailTemplateCommandValidator : AbstractValidator<CreateEmailTemplateCommand>
{
    public CreateEmailTemplateCommandValidator()
    {
        RuleFor(x => x.EmailTemplateTypeId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Template.MaxNameLength);
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(Template.MaxSubjectLength);
        RuleFor(x => x.BodyHtml).NotEmpty().MaximumLength(Template.MaxBodyLength);
    }
}

public sealed class UpdateEmailTemplateCommandValidator : AbstractValidator<UpdateEmailTemplateCommand>
{
    public UpdateEmailTemplateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.EmailTemplateTypeId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Template.MaxNameLength);
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(Template.MaxSubjectLength);
        RuleFor(x => x.BodyHtml).NotEmpty().MaximumLength(Template.MaxBodyLength);
    }
}

/// <summary>
/// Shared checks of the create and update commands (SPEC 71): the type must be an active type of the
/// company, the name must be free in the company, the subject and the body may only use the closed list
/// of variables and the body is stored sanitized.
/// </summary>
public sealed class EmailTemplateWriteGuard(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IEmailTemplateRenderer renderer,
    IHtmlSanitizer sanitizer)
{
    internal async Task<(TemplateType? Type, Response<string>? Error)> ResolveTypeAsync(Guid companyId, Guid typeId, CancellationToken cancellationToken)
    {
        var type = await unitOfWork.GetRepository<TemplateType>().GetAsync(typeId);
        if (type is null || type.CompanyId != companyId || type.GcRecord != 0 || !type.IsActive)
        {
            return (null, EmailTemplateErrors.TypeNotFound<string>());
        }

        return (type, null);
    }

    internal async Task<Response<string>?> ValidateAsync(
        Guid companyId,
        Guid? exceptTemplateId,
        string name,
        string subject,
        string bodyHtml,
        CancellationToken cancellationToken)
    {
        var trimmedName = name.Trim();
        var duplicate = (await unitOfWork.GetRepository<Template>().GetAllIncludingDeletedAsync(
            x => x.CompanyId == companyId
                && x.Name == trimmedName
                && x.GcRecord == 0
                && (!exceptTemplateId.HasValue || x.Id != exceptTemplateId.Value)))
            .Any();
        if (duplicate)
        {
            return EmailTemplateErrors.DuplicateName<string>();
        }

        var unknown = renderer.Validate(subject, bodyHtml);
        if (unknown.Count > 0)
        {
            return EmailTemplateErrors.UnknownVariables<string>(unknown);
        }

        return null;
    }

    internal string Sanitize(string bodyHtml) => sanitizer.Sanitize(bodyHtml);
}

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

        var (type, typeError) = await guard.ResolveTypeAsync(companyId, request.EmailTemplateTypeId, cancellationToken);
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

/// <summary>Handles <see cref="RestoreEmailTemplateCommand"/> with <see cref="SoftDeleteRestorer"/>.</summary>
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
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<Template>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.Name == entity.Name),
            cancellationToken: cancellationToken);
    }
}

/// <summary>Shared SQL of the template queries (SPEC 71).</summary>
internal static class EmailTemplateQuerySql
{
    internal const string Select = """
        SELECT
            t.Id,
            t.CompanyId,
            t.EmailTemplateTypeId,
            tt.Name AS EmailTemplateTypeName,
            tt.Code AS EmailTemplateTypeCode,
            t.Name,
            t.Subject,
            t.BodyHtml,
            t.IsActive,
            t.Created AS CreatedAt
        FROM admin.emailtemplates t
        JOIN admin.emailtemplatetypes tt ON tt.Id = t.EmailTemplateTypeId
        """;
}

/// <summary>Handles <see cref="GetEmailTemplatesQuery"/> with Dapper.</summary>
public sealed class GetEmailTemplatesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetEmailTemplatesQuery, Response<PagedResult<EmailTemplateDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<EmailTemplateDto>>> Handle(GetEmailTemplatesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateErrors.CompanyRequired<PagedResult<EmailTemplateDto>>();
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", currentUserService.CompanyId);
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE t.CompanyId = @CompanyId AND t.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            where.Append(" AND (t.Name ILIKE @Search OR t.Subject ILIKE @Search)");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        if (request.EmailTemplateTypeId is { } typeId && typeId != Guid.Empty)
        {
            where.Append(" AND t.EmailTemplateTypeId = @EmailTemplateTypeId");
            parameters.Add("EmailTemplateTypeId", typeId);
        }

        if (request.IsActive.HasValue)
        {
            where.Append(" AND t.IsActive = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        var sql = $"""
            {EmailTemplateQuerySql.Select}
            {where}
            ORDER BY t.Name
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM admin.emailtemplates t
            {where};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<EmailTemplateDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<EmailTemplateDto>>
        {
            IsSuccess = true,
            Message = "Email templates retrieved successfully.",
            Data = new PagedResult<EmailTemplateDto> { Items = items, PageNumber = pageNumber, PageSize = pageSize, TotalCount = total }
        };
    }
}

/// <summary>Handles <see cref="GetEmailTemplateByIdQuery"/> with Dapper.</summary>
public sealed class GetEmailTemplateByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetEmailTemplateByIdQuery, Response<EmailTemplateDto>>
{
    public async Task<Response<EmailTemplateDto>> Handle(GetEmailTemplateByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateErrors.CompanyRequired<EmailTemplateDto>();
        }

        const string sql = $"""
            {EmailTemplateQuerySql.Select}
            WHERE t.Id = @Id
              AND t.CompanyId = @CompanyId
              AND t.GcRecord = 0;
            """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<EmailTemplateDto>(new CommandDefinition(
            sql,
            new { request.Id, CompanyId = currentUserService.CompanyId },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return EmailTemplateErrors.NotFound<EmailTemplateDto>();
        }

        return new Response<EmailTemplateDto>
        {
            IsSuccess = true,
            Message = "Email template retrieved successfully.",
            Data = row
        };
    }
}
