using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonAddresses.Commands;

/// <summary>
/// Validation rules for <see cref="CreatePersonAddressCommand"/>.
/// </summary>
public sealed class CreatePersonAddressValidator : AbstractValidator<CreatePersonAddressCommand>
{
    /// <summary>
    /// Initializes validator rules for creating customer addresses.
    /// </summary>
    public CreatePersonAddressValidator()
    {
        RuleFor(x => x.PersonId)
            .NotEmpty().WithMessage("Person id is required.");

        RuleFor(x => x.AddressLine1)
            .NotEmpty().WithMessage("Address line 1 is required.")
            .MaximumLength(200).WithMessage("Address line 1 cannot exceed 200 characters.");

        RuleFor(x => x.AddressLine2)
            .MaximumLength(200).WithMessage("Address line 2 cannot exceed 200 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.AddressLine2));

        RuleFor(x => x.ZipCode)
            .NotEmpty().WithMessage("Zip code is required.")
            .MaximumLength(20).WithMessage("Zip code cannot exceed 20 characters.");

        RuleFor(x => x.StreetTypeId)
            .NotEmpty().WithMessage("Street type is required.");

        RuleFor(x => x.CountryId)
            .NotEmpty().WithMessage("Country is required.");

        RuleFor(x => x.ProvinceId)
            .NotEmpty().WithMessage("Province is required.");

        RuleFor(x => x.MunicipalityId)
            .NotEmpty().WithMessage("Municipality is required.");

        RuleFor(x => x.RegionId)
            .NotEqual(Guid.Empty)
            .When(x => x.RegionId.HasValue)
            .WithMessage("Region id cannot be an empty guid.");
    }
}
