using FluentValidation;

namespace JOIN.Application.UseCases.Security.SystemOptions.Commands;

public sealed class DeleteSystemOptionCommandValidator : AbstractValidator<DeleteSystemOptionCommand>
{
    public DeleteSystemOptionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty();
    }
}
