namespace JOIN.Application.UseCases.Admin.Customers.Queries;

/// <summary>
/// Shared SQL projection for customer read queries.
/// </summary>
internal static class CustomerQuerySql
{
    internal const string SelectProjection = """
        SELECT
            cust.Id,
            cust.CompanyId,
            cust.PersonId,
            cust.UserId,
            cust.CustomerCode,
            cust.PersonLifecycleStage,
            CASE cust.PersonLifecycleStage
                WHEN 1 THEN 'En contacto'
                WHEN 2 THEN 'En proceso'
                WHEN 3 THEN 'cliente'
                WHEN 4 THEN 'ClienteInactivo'
                ELSE 'Desconocido'
            END AS PersonLifecycleStageName,
            CASE
                WHEN p.PersonType = 1 THEN LTRIM(RTRIM(CONCAT(
                    p.FirstName, ' ',
                    ISNULL(p.MiddleName + ' ', ''),
                    ISNULL(p.LastName, ''), ' ',
                    ISNULL(p.SecondLastName, ''))))
                ELSE COALESCE(NULLIF(p.CommercialName, ''), p.FirstName, '')
            END AS PersonName,
            u.Email AS UserEmail,
            cust.IsActive,
            cust.ActivatedAt,
            cust.DeactivatedAt,
            cust.Created AS CreatedAt
        FROM Admin.Customers cust
        INNER JOIN Admin.Persons p
            ON p.Id = cust.PersonId
           AND p.CompanyId = @TenantId
           AND p.GcRecord = 0
        INNER JOIN Security.Users u
            ON u.Id = cust.UserId
        """;
}
