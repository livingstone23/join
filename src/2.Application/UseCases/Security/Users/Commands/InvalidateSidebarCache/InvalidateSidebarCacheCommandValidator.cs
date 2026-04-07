using FluentValidation;

namespace JOIN.Application.UseCases.Security.Users.Commands.InvalidateSidebarCache;

/// <summary>
/// Validates the payload used to invalidate a cached sidebar menu.
/// </summary>
public sealed class InvalidateSidebarCacheCommandValidator : AbstractValidator<InvalidateSidebarCacheCommand>
{
    public InvalidateSidebarCacheCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");
    }
}
