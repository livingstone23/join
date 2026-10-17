using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using MediatR;
using Settings = JOIN.Domain.Customers.Leads.LeadCompanySettings;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadCompanySettings.Queries;

/// <summary>
/// <c>GET /LeadCompanySettings</c> (SPEC 59): the company's row, or the defaults (<c>IsDefault = true</c>) when it has none.
/// </summary>
public sealed record GetLeadCompanySettingsQuery(Guid? CompanyId = null) : IRequest<Response<LeadCompanySettingsDto>>;

/// <summary>Handles <see cref="GetLeadCompanySettingsQuery"/>.</summary>
public sealed class GetLeadCompanySettingsQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetLeadCompanySettingsQuery, Response<LeadCompanySettingsDto>>
{
    public async Task<Response<LeadCompanySettingsDto>> Handle(GetLeadCompanySettingsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CommercialCatalogQuery.CompanyRequired<LeadCompanySettingsDto>();
        }

        var companyId = TenantResolver.Resolve(currentUserService, request.CompanyId);
        const string sql = """
            SELECT
                s.Id,
                s.CompanyId,
                s.DuplicateOpenLeadPolicy,
                s.RequireIdentificationForCustomer,
                s.CodePrefix,
                s.CodeSequenceLength
            FROM customer.leadcompanysettings s
            WHERE s.CompanyId = @CompanyId
              AND s.GcRecord = 0;
            """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<LeadCompanySettingsDto>(new CommandDefinition(
            sql, new { CompanyId = companyId }, cancellationToken: cancellationToken));

        return new Response<LeadCompanySettingsDto>
        {
            IsSuccess = true,
            Message = "Lead settings retrieved successfully.",
            Data = row ?? LeadCompanySettingsMapping.ToDto(Settings.Defaults(companyId), isDefault: true)
        };
    }
}

/// <summary>Entity → DTO for the company lead settings.</summary>
public static class LeadCompanySettingsMapping
{
    public static LeadCompanySettingsDto ToDto(Settings settings, bool isDefault) => new()
    {
        Id = isDefault ? null : settings.Id,
        CompanyId = settings.CompanyId,
        DuplicateOpenLeadPolicy = (int)settings.DuplicateOpenLeadPolicy,
        RequireIdentificationForCustomer = settings.RequireIdentificationForCustomer,
        CodePrefix = settings.CodePrefix,
        CodeSequenceLength = settings.CodeSequenceLength,
        IsDefault = isDefault
    };
}
