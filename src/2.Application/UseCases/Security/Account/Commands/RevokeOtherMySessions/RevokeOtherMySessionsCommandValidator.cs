using FluentValidation;

namespace JOIN.Application.UseCases.Security.Account.Commands.RevokeOtherMySessions;

/// <summary>
/// Validates the inbound <see cref="RevokeOtherMySessionsCommand"/> before the handler runs.
/// Empty-body command today; reserved for future filtering/query-string parameters.
/// </summary>
public sealed class RevokeOtherMySessionsCommandValidator : AbstractValidator<RevokeOtherMySessionsCommand>
{
    /// <summary>
    /// Builds the validator. No payload-level rules yet.
    /// </summary>
    public RevokeOtherMySessionsCommandValidator()
    {
    }
}
