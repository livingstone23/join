using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using JOIN.Application.Interface;
using JOIN.Application.Services.Email;
using JOIN.Application.UseCases.Common.EmailTemplateTypes.Commands;
using MediatR;

namespace JOIN.Application.UseCases.Common.EmailTemplateTypes.Queries;

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

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted) ? string.Empty : "AND GcRecord = 0";
        var sql = $"""
            SELECT Id, GcRecord, CompanyId, Code, Name, SortOrder, IsActive, Created AS CreatedAt
            FROM admin.emailtemplatetypes
            WHERE CompanyId = @CompanyId
              {deleted}
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
