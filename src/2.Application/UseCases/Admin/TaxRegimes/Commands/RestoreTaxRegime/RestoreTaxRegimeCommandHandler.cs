using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Commands;

/// <summary>
/// Handles <see cref="RestoreTaxRegimeCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the tax regime's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreTaxRegimeCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreTaxRegimeCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreTaxRegimeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<TaxRegime>(
            request.Id,
            request.CompanyId,
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<TaxRegime>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && (x.Code == entity.Code || x.Name == entity.Name)),
            cancellationToken: cancellationToken);
    }
}
