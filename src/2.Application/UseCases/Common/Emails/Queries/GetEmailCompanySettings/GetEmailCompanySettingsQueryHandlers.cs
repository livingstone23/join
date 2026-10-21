using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.Emails;
using JOIN.Application.Interface;
using JOIN.Application.Services.Email;
using MediatR;

namespace JOIN.Application.UseCases.Common.Emails.Queries;

/// <summary>Handles <see cref="GetEmailCompanySettingsQuery"/> with Dapper.</summary>
public sealed class GetEmailCompanySettingsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetEmailCompanySettingsQuery, Response<EmailCompanySettingsDto>>
{
    public async Task<Response<EmailCompanySettingsDto>> Handle(GetEmailCompanySettingsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<EmailCompanySettingsDto>.Error(PersonEmailErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        const string sql = """
            SELECT Id, CompanyId, FromEmail, FromName, MaxEmailsPerUserPerDay, MaxAttachmentsTotalBytes
            FROM admin.emailcompanysettings
            WHERE CompanyId = @CompanyId AND GcRecord = 0;
            """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<EmailCompanySettingsDto>(new CommandDefinition(
            sql, new { currentUserService.CompanyId }, cancellationToken: cancellationToken));

        return row is null
            ? Response<EmailCompanySettingsDto>.Error(PersonEmailErrorCodes.EmailSettingsNotConfigured, ["The company has no email settings row yet."])
            : new Response<EmailCompanySettingsDto> { IsSuccess = true, Message = "Email settings retrieved successfully.", Data = row };
    }
}
