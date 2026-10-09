using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.RestoreUserCompany;

/// <summary>
/// Restores a user's removed membership in a company together with the role assignments removed with it
/// (SPEC 41, Etapa 3). Only a <c>SuperAdmin</c> can restore.
/// </summary>
/// <param name="UserId">The user whose membership is restored.</param>
/// <param name="CompanyId">The company of the membership (also the explicit tenant for <see cref="TenantResolver"/>).</param>
public sealed record RestoreUserCompanyCommand(Guid UserId, Guid CompanyId) : ITransactionalCommand<Response<Guid>>;
