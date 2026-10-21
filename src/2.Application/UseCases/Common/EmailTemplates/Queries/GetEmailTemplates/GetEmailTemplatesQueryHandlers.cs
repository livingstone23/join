using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using JOIN.Application.Interface;
using JOIN.Application.Services.Email;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Common.EmailTemplates.Queries;

/// <summary>Shared SQL of the template queries (SPEC 71).</summary>
internal static class EmailTemplateQuerySql
{
    internal const string Select = """
        SELECT
            t.Id,
            t.GcRecord,
            t.CompanyId,
            t.EmailTemplateTypeId,
            tt.Name AS EmailTemplateTypeName,
            tt.Code AS EmailTemplateTypeCode,
            t.Name,
            t.Subject,
            t.BodyHtml,
            t.IsActive,
            t.Created AS CreatedAt,
            t.LastModified AS ModifiedAt
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

        var where = new StringBuilder("WHERE t.CompanyId = @CompanyId");
        if (!SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted))
        {
            where.Append(" AND t.GcRecord = 0");
        }

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

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted) ? string.Empty : "AND t.GcRecord = 0";
        var sql = $"""
            {EmailTemplateQuerySql.Select}
            WHERE t.Id = @Id
              AND t.CompanyId = @CompanyId
              {deleted};
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

/// <summary>Handles <see cref="LookupEmailTemplatesQuery"/> with Dapper.</summary>
public sealed class LookupEmailTemplatesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<LookupEmailTemplatesQuery, Response<IReadOnlyList<EmailTemplateLookupItemDto>>>
{
    /// <summary>Maximum number of templates the picker returns (SPEC 71, scope C.11).</summary>
    private const int MaxResults = 50;

    public async Task<Response<IReadOnlyList<EmailTemplateLookupItemDto>>> Handle(LookupEmailTemplatesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateErrors.CompanyRequired<IReadOnlyList<EmailTemplateLookupItemDto>>();
        }

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", currentUserService.CompanyId);
        parameters.Add("MaxResults", MaxResults);

        var filters = "WHERE t.CompanyId = @CompanyId AND t.GcRecord = 0 AND t.IsActive = TRUE";

        if (request.EmailTemplateTypeId is { } typeId && typeId != Guid.Empty)
        {
            filters += " AND t.EmailTemplateTypeId = @EmailTemplateTypeId";
            parameters.Add("EmailTemplateTypeId", typeId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            filters += " AND (t.Name ILIKE @Search OR t.Subject ILIKE @Search)";
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        var sql = $"""
            SELECT
                t.Id,
                t.Name,
                t.Subject,
                t.EmailTemplateTypeId,
                tt.Name AS EmailTemplateTypeName,
                tt.Code AS EmailTemplateTypeCode
            FROM admin.emailtemplates t
            JOIN admin.emailtemplatetypes tt ON tt.Id = t.EmailTemplateTypeId
            {filters}
            ORDER BY tt.SortOrder, tt.Name, t.Name
            LIMIT @MaxResults;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<EmailTemplateLookupItemDto>(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken));

        return new Response<IReadOnlyList<EmailTemplateLookupItemDto>>
        {
            IsSuccess = true,
            Message = "Email templates retrieved successfully.",
            Data = rows.AsList()
        };
    }
}

/// <summary>Handles <see cref="GetEmailTemplateVariablesQuery"/>.</summary>
public sealed class GetEmailTemplateVariablesQueryHandler
    : IRequestHandler<GetEmailTemplateVariablesQuery, Response<IReadOnlyList<EmailTemplateVariableDto>>>
{
    public Task<Response<IReadOnlyList<EmailTemplateVariableDto>>> Handle(GetEmailTemplateVariablesQuery request, CancellationToken cancellationToken)
    {
        var variables = EmailTemplateVariables.All
            .Select(v => new EmailTemplateVariableDto { Key = v.Key, Description = v.Description })
            .ToList();

        return Task.FromResult(new Response<IReadOnlyList<EmailTemplateVariableDto>>
        {
            IsSuccess = true,
            Message = "Email template variables retrieved successfully.",
            Data = variables
        });
    }
}
