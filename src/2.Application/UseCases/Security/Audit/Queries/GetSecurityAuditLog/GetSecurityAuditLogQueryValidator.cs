using FluentValidation;

namespace JOIN.Application.UseCases.Security.Audit.Queries.GetSecurityAuditLog;

/// <summary>
/// FluentValidation guard for <see cref="GetSecurityAuditLogQuery"/>.
/// Entity / Action enum parsing is enforced by the handler (it returns the
/// <c>INVALID_ENTITY</c> / <c>INVALID_ACTION</c> error codes the spec mandates);
/// this validator only bounds the paging parameters and the date range.
/// </summary>
public sealed class GetSecurityAuditLogQueryValidator : AbstractValidator<GetSecurityAuditLogQuery>
{
    public GetSecurityAuditLogQueryValidator()
    {
        RuleFor(q => q.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, 100);

        RuleFor(q => q)
            .Must(q => q.FromDate is null || q.ToDate is null || q.FromDate <= q.ToDate)
            .WithMessage("FromDate must be on or before ToDate.");
    }
}
