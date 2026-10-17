using JOIN.Application.Common;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadStages;

/// <summary>
/// Keeps exactly one live Initial, Won and Lost stage per company (SPEC 59 rule 2): no second one, never none, and the
/// Won/Lost system stages are renamed but not deleted, deactivated or re-coded. The filtered unique indexes
/// <c>UX_LeadStages_Company_Initial</c> / <c>UX_LeadStages_Company_WonLost</c> back the "no second one" rule under
/// concurrency.
/// </summary>
public sealed class LeadStageKindCoordinator
{
    /// <summary>Creating a stage: a second live Initial/Won/Lost is a duplicate.</summary>
    public Response<T>? ValidateCreate<T>(LeadStageKind kind, IEnumerable<LeadStage> live) =>
        kind != LeadStageKind.Open && live.Any(s => s.Kind == kind) ? DuplicateKind<T>(kind) : null;

    /// <summary>Updating <paramref name="existing"/> to the requested code, kind and state.</summary>
    public Response<T>? ValidateUpdate<T>(LeadStage existing, string code, LeadStageKind kind, bool isActive, IEnumerable<LeadStage> live)
    {
        if (existing.IsSystem && (!string.Equals(existing.Code, CommercialCode.Normalize(code), StringComparison.OrdinalIgnoreCase) || !isActive))
        {
            return Response<T>.Error(LeadSettingsErrorCodes.LeadStageSystem, ["The Won and Lost stages can be renamed, but their code cannot change and they cannot be deactivated."]);
        }

        if (existing.IsUniqueKind && kind != existing.Kind)
        {
            return KindRequired<T>(existing.Kind);
        }

        if (existing.Kind == LeadStageKind.Initial && !isActive)
        {
            return KindRequired<T>(LeadStageKind.Initial);
        }

        return kind != existing.Kind && kind != LeadStageKind.Open && live.Any(s => s.Id != existing.Id && s.Kind == kind)
            ? DuplicateKind<T>(kind)
            : null;
    }

    /// <summary>Deleting a stage: system stages and the Initial stage stay.</summary>
    public Response<T>? ValidateDelete<T>(LeadStage existing)
    {
        if (existing.IsSystem)
        {
            return Response<T>.Error(LeadSettingsErrorCodes.LeadStageSystem, ["The Won and Lost stages cannot be deleted."]);
        }

        return existing.Kind == LeadStageKind.Initial ? KindRequired<T>(LeadStageKind.Initial) : null;
    }

    private static Response<T> DuplicateKind<T>(LeadStageKind kind) =>
        Response<T>.Error(LeadSettingsErrorCodes.DuplicateKey, [$"The company already has a live {kind} stage."]);

    private static Response<T> KindRequired<T>(LeadStageKind kind) =>
        Response<T>.Error(LeadSettingsErrorCodes.LeadStageKindRequired, [$"The company must keep one active {kind} stage."]);
}
