using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetSecurityActivity;

/// <summary>
/// Validates inbound values for <see cref="GetSecurityActivityQuery"/>. Clamping
/// (instead of pure rejection) happens inside the handler.
/// </summary>
public sealed class GetSecurityActivityQueryValidator : AbstractValidator<GetSecurityActivityQuery>
{
    /// <summary>
    /// Builds the validator.
    /// </summary>
    public GetSecurityActivityQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithErrorCode("PAGE_NUMBER_INVALID")
            .WithMessage("Page number must be 1 or greater.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithErrorCode("PAGE_SIZE_INVALID")
            .WithMessage("Page size must be between 1 and 100.");
    }
}
