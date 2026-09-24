using FluentValidation;
using JOIN.Application.Common;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Security.Audit.Queries.GetSecurityAuditLog;

/// <summary>
/// FluentValidation guard for <see cref="GetSecurityAuditLogQuery"/>.
/// Entity / Action enum parsing is enforced by the handler (it returns the
/// <c>INVALID_ENTITY</c> / <c>INVALID_ACTION</c> error codes the spec mandates);
/// this validator only bounds the paging parameters (against the shared
/// <see cref="PaginationSettings"/>) and the date range.
/// </summary>
public sealed class GetSecurityAuditLogQueryValidator : AbstractValidator<GetSecurityAuditLogQuery>
{
    public GetSecurityAuditLogQueryValidator(IOptions<PaginationSettings> paginationOptions)
    {
        var paginationSettings = paginationOptions.Value ?? new PaginationSettings();

        RuleFor(q => q.PageNumber)
            .GreaterThanOrEqualTo(1);

        RuleFor(q => q.PageSize)
            .InclusiveBetween(paginationSettings.MinPageSize, paginationSettings.MaxPageSize);

        RuleFor(q => q)
            .Must(q => q.FromDate is null || q.ToDate is null || q.FromDate <= q.ToDate)
            .WithMessage("FromDate must be on or before ToDate.");
    }
}
