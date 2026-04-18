using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;

/// <summary>
/// Defines validation rules for tenant ticket default configuration updates.
/// </summary>
public sealed class UpdateTicketCompanyDefaultCommandValidator : AbstractValidator<UpdateTicketCompanyDefaultCommand>
{
    public UpdateTicketCompanyDefaultCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEqual(Guid.Empty).WithMessage("The configuration identifier is required.");

        RuleFor(x => x.StartCode)
            .NotEmpty().WithMessage("The start code is required.")
            .MaximumLength(20).WithMessage("The start code cannot exceed 20 characters.");

        RuleFor(x => x.CodeSequenceLength)
            .GreaterThan(0).WithMessage("CodeSequenceLength must be greater than zero.");

        RuleFor(x => x.TicketStatusDefaultId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("The default status identifier is invalid.");

        RuleFor(x => x.TicketComplexityDefaultId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("The default complexity identifier is invalid.");

        RuleFor(x => x.TimeUnitDefaultId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("The default time unit identifier is invalid.");

        RuleFor(x => x.AreaDefaultId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("The default area identifier is invalid.");

        RuleFor(x => x.ProjectDefaultId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("The default project identifier is invalid.");

        RuleFor(x => x.ChannelDefaultId)
            .Must(value => !value.HasValue || value.Value != Guid.Empty)
            .WithMessage("The default communication channel identifier is invalid.");
    }
}
