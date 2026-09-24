using FluentValidation;
using JOIN.Application.Common;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetSecurityActivity;

/// <summary>
/// Validates inbound values for <see cref="GetSecurityActivityQuery"/>. Clamping
/// (instead of pure rejection) happens inside the handler.
/// </summary>
public sealed class GetSecurityActivityQueryValidator : AbstractValidator<GetSecurityActivityQuery>
{
    /// <summary>
    /// Builds the validator, bounding <c>PageSize</c> against the shared
    /// <see cref="PaginationSettings"/> instead of a hardcoded range.
    /// </summary>
    public GetSecurityActivityQueryValidator(IOptions<PaginationSettings> paginationOptions)
    {
        var paginationSettings = paginationOptions.Value ?? new PaginationSettings();

        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithErrorCode("PAGE_NUMBER_INVALID")
            .WithMessage("Page number must be 1 or greater.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(paginationSettings.MinPageSize, paginationSettings.MaxPageSize)
            .WithErrorCode("PAGE_SIZE_INVALID")
            .WithMessage($"Page size must be between {paginationSettings.MinPageSize} and {paginationSettings.MaxPageSize}.");
    }
}
