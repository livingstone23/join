using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Countries.Commands;

/// <summary>
/// Handles <see cref="RestoreCountryCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the country's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreCountryCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreCountryCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreCountryCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<Country>(
            request.Id,
            request.CompanyId,
            cancellationToken: cancellationToken);
    }
}
