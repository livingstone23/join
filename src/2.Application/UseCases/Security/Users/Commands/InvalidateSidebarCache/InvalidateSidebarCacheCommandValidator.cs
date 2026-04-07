using FluentValidation;

namespace JOIN.Application.UseCases.Security.Users.Commands.InvalidateSidebarCache;

/// <summary>
/// Validates the payload used to clear cached sidebar and permission entries.
/// </summary>
public sealed class CleanCacheCommandValidator : AbstractValidator<CleanCacheCommand>
{
    public CleanCacheCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("UserId is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");

        RuleFor(x => x.TargetKey)
            .NotEmpty()
            .WithMessage("CacheKey is required. Use 'sidebar', 'permission', or 'all'.")
            .Must(BeSupportedCacheKey)
            .WithMessage("CacheKey must be one of: 'sidebar', 'permission', or 'all'.");
    }

    private static bool BeSupportedCacheKey(string cacheKey)
        => cacheKey.Trim().ToLowerInvariant() is "sidebar" or "permission" or "permissions" or "all";
}
