namespace JOIN.Application.UseCases.Customers.Customers.Queries;

/// <summary>
/// Shared SQL projection for customer read queries (schema <c>customer</c>, SPEC 55).
/// </summary>
internal static class CustomerQuerySql
{
    internal const string PersonNameExpression = """
        CASE
            WHEN p.PersonType = 1 THEN LTRIM(RTRIM(CONCAT(
                p.FirstName, ' ',
                COALESCE(p.MiddleName || ' ', ''),
                COALESCE(p.LastName, ''), ' ',
                COALESCE(p.SecondLastName, ''))))
            ELSE COALESCE(NULLIF(p.CommercialName, ''), p.FirstName, '')
        END
        """;

    internal const string FromClause = """
        FROM customer.customers cust
        INNER JOIN Admin.Persons p
            ON p.Id = cust.PersonId
           AND p.CompanyId = @TenantId
           AND p.GcRecord = 0
        INNER JOIN Security.Users u
            ON u.Id = cust.UserId
        INNER JOIN customer.customerstages st
            ON st.Id = cust.CustomerStageId
        """;

    internal const string SelectProjection = $"""
        SELECT
            cust.Id,
            cust.GcRecord,
            cust.CompanyId,
            cust.PersonId,
            cust.UserId,
            cust.CustomerCode,
            cust.CustomerStageId,
            st.Code AS CustomerStageCode,
            st.Name AS CustomerStageName,
            {PersonNameExpression} AS PersonName,
            u.Email AS UserEmail,
            cust.IsActive,
            cust.ActivatedAt,
            cust.DeactivatedAt,
            cust.Created AS CreatedAt,
            lh.Action AS LastStatusAction,
            lh.Source AS LastStatusSource,
            lh.OccurredAt AS LastStatusAt
        {FromClause}
        LEFT JOIN LATERAL (
            SELECT h.Action, h.Source, h.OccurredAt
            FROM customer.customerstatushistory h
            WHERE h.CustomerId = cust.Id
              AND h.GcRecord = 0
            ORDER BY h.OccurredAt DESC
            LIMIT 1
        ) lh ON TRUE
        """;
}
