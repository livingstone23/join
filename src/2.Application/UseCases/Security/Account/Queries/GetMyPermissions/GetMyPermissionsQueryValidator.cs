using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetMyPermissions;

/// <summary>
/// Reserved for future query-string validation. Tenant checks live in the handler
/// because the JWT-resolved <c>CompanyId</c> is the source of truth (no
/// <c>?companyId=</c> override is allowed).
/// </summary>
public sealed class GetMyPermissionsQueryValidator : AbstractValidator<GetMyPermissionsQuery>
{
    /// <summary>
    /// Builds the validator with no payload rules.
    /// </summary>
    public GetMyPermissionsQueryValidator()
    {
    }
}
