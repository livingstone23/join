using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Persons.Queries;

/// <summary>
/// Handles <see cref="GetPersonLookupQuery"/> with one Dapper query (SPEC 54): active, live persons of the company
/// whose names, full name, identification number or customer code contain the term, one row per person (the
/// customer is unique per person, SPEC 55), exact identification/code matches first, then active customers,
/// inactive customers and plain persons.
/// </summary>
public sealed class GetPersonLookupQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonLookupQuery, Response<IReadOnlyList<PersonLookupItemDto>>>
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;
    public const int MinTermLength = 2;

    public const string Sql = """
        SELECT
            p.Id AS PersonId,
            p.PersonType,
            CASE
                WHEN p.PersonType = 2 AND NULLIF(p.CommercialName, '') IS NOT NULL THEN p.CommercialName
                ELSE CONCAT_WS(' ', NULLIF(p.FirstName, ''), NULLIF(p.MiddleName, ''), NULLIF(p.LastName, ''), NULLIF(p.SecondLastName, ''))
            END AS FullName,
            p.CommercialName,
            p.IdentificationTypeId,
            it.Name AS IdentificationTypeName,
            p.IdentificationNumber,
            (cust.Id IS NOT NULL) AS IsCustomer,
            COALESCE(cust.IsActive, FALSE) AS IsCustomerActive,
            cust.Id AS CustomerId,
            cust.CustomerCode,
            cust.CustomerStageId,
            st.Code AS CustomerStageCode,
            st.Name AS CustomerStageName
        FROM Admin.Persons p
        LEFT JOIN Admin.IdentificationTypes it
            ON it.Id = p.IdentificationTypeId
        LEFT JOIN customer.customers cust
            ON cust.CompanyId = p.CompanyId
           AND cust.PersonId = p.Id
           AND cust.GcRecord = 0
        LEFT JOIN customer.customerstages st
            ON st.Id = cust.CustomerStageId
        WHERE p.CompanyId = @TenantId
          AND p.GcRecord = 0
          AND p.IsActive = TRUE
          AND (
                p.FirstName ILIKE @Pattern
             OR p.MiddleName ILIKE @Pattern
             OR p.LastName ILIKE @Pattern
             OR p.SecondLastName ILIKE @Pattern
             OR p.CommercialName ILIKE @Pattern
             OR p.IdentificationNumber ILIKE @Pattern
             OR CONCAT_WS(' ', NULLIF(p.FirstName, ''), NULLIF(p.MiddleName, ''), NULLIF(p.LastName, ''), NULLIF(p.SecondLastName, '')) ILIKE @Pattern
             OR cust.CustomerCode ILIKE @Pattern
          )
        ORDER BY
            CASE WHEN LOWER(p.IdentificationNumber) = LOWER(@Term) OR LOWER(cust.CustomerCode) = LOWER(@Term) THEN 0 ELSE 1 END,
            CASE WHEN cust.Id IS NULL THEN 2 WHEN cust.IsActive THEN 0 ELSE 1 END,
            FullName ASC,
            p.Id ASC
        LIMIT @Limit;
        """;

    public async Task<Response<IReadOnlyList<PersonLookupItemDto>>> Handle(
        GetPersonLookupQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<PersonLookupItemDto>>.Error(
                "COMPANY_REQUIRED",
                ["The X-Company-Id header is required."]);
        }

        var term = request.Term?.Trim() ?? string.Empty;
        if (term.Length < MinTermLength)
        {
            return Response<IReadOnlyList<PersonLookupItemDto>>.Error(
                "INVALID_SEARCH_TERM",
                [$"term must have at least {MinTermLength} characters."]);
        }

        var limit = Math.Clamp(request.Limit ?? DefaultLimit, 1, MaxLimit);

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<PersonLookupItemDto>(new CommandDefinition(
                Sql,
                new
                {
                    TenantId = currentUserService.CompanyId,
                    Term = term,
                    Pattern = $"%{EscapeLikePattern(term)}%",
                    Limit = limit
                },
                cancellationToken: cancellationToken)))
            .ToList();

        return new Response<IReadOnlyList<PersonLookupItemDto>>
        {
            IsSuccess = true,
            Message = "Persons retrieved successfully.",
            Data = items
        };
    }

    /// <summary>
    /// Escapes <c>\</c>, <c>%</c> and <c>_</c> so they match literally (PostgreSQL's default LIKE escape is <c>\</c>).
    /// </summary>
    public static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
