using FluentValidation;



namespace JOIN.Application.UseCases.Admin.Persons.Commands;



/// <summary>
/// Defines validation rules for <see cref="DeletePersonCommand"/>.
/// </summary>
public class DeletePersonCommandValidator : AbstractValidator<DeletePersonCommand>
{
    public DeletePersonCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Person id is required.");
    }
}
