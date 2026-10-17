using JOIN.Application.Common;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.Leads.Common;

/// <summary>
/// Stage moves of an open lead (SPEC 61 rule 3, B.10): the target must be an active Initial/Open stage and either the
/// <b>next</b> active one by <c>SortOrder</c> (no skipping) or any earlier one. Won and Lost have their own actions
/// (SPEC 63). The order is always the current one, so reordering the stages changes which one is next.
/// </summary>
public sealed class LeadStageAdvanceCoordinator
{
    public Response<T>? Validate<T>(LeadStage current, LeadStage? target, IReadOnlyCollection<LeadStage> stages)
    {
        if (target is null || !target.IsActive || target.GcRecord != 0 || target.Kind is not (LeadStageKind.Initial or LeadStageKind.Open))
        {
            return Response<T>.Error(LeadErrorCodes.InvalidLeadStage, ["The stage must be an active initial or open stage of the company."]);
        }

        if (Compare(target, current) < 0)
        {
            return null;
        }

        var next = stages
            .Where(s => s.GcRecord == 0 && s.IsActive && s.Kind is LeadStageKind.Initial or LeadStageKind.Open && Compare(s, current) > 0)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Code, StringComparer.Ordinal)
            .FirstOrDefault();

        return next?.Id == target.Id
            ? null
            : Response<T>.Error(LeadErrorCodes.StageSkipNotAllowed, ["A lead moves to the next stage or back to an earlier one."]);
    }

    private static int Compare(LeadStage a, LeadStage b)
    {
        var bySort = a.SortOrder.CompareTo(b.SortOrder);
        return bySort != 0 ? bySort : string.CompareOrdinal(a.Code, b.Code);
    }
}
