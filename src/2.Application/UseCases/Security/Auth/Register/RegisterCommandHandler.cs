using FluentValidation.Results;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Auth.Register;

/// <summary>
/// Handles self-registration requests for new application users.
/// </summary>
/// <param name="userManager">ASP.NET Core Identity manager used to create the user account.</param>
public class RegisterCommandHandler(UserManager<ApplicationUser> userManager)
    : IRequestHandler<RegisterCommand, Response<RegisterResponseDto>>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;

    /// <summary>
    /// Creates a new active user without company assignments.
    /// </summary>
    /// <param name="request">The registration request payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the created user identifier.</returns>
    /// <exception cref="ValidationException">Thrown when the registration payload violates business or Identity rules.</exception>
    public async Task<Response<RegisterResponseDto>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim();
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        var existingUser = await _userManager.FindByEmailAsync(normalizedEmail);
        if (existingUser is not null)
        {
            throw BuildValidationException(nameof(RegisterCommand.Email), "A user with the same email already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            FirstName = firstName,
            LastName = lastName,
            IsActive = true,
            Created = DateTime.UtcNow,
            CreatedBy = normalizedEmail,
            GcRecord = 0
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            throw BuildValidationException(result.Errors);
        }

        return new Response<RegisterResponseDto>
        {
            IsSuccess = true,
            Message = "User registered successfully.",
            Data = new RegisterResponseDto
            {
                UserId = user.Id,
                Email = user.Email ?? normalizedEmail
            }
        };
    }

    /// <summary>
    /// Creates a validation exception for a single property error.
    /// </summary>
    /// <param name="propertyName">The property associated with the validation failure.</param>
    /// <param name="errorMessage">The validation error message.</param>
    /// <returns>A populated <see cref="ValidationException"/> instance.</returns>
    private static ValidationException BuildValidationException(string propertyName, string errorMessage)
    {
        return new ValidationException(
        [
            new ValidationFailure(propertyName, errorMessage)
        ]);
    }

    /// <summary>
    /// Creates a validation exception from ASP.NET Identity errors.
    /// </summary>
    /// <param name="errors">The identity errors produced during user creation.</param>
    /// <returns>A populated <see cref="ValidationException"/> instance.</returns>
    private static ValidationException BuildValidationException(IEnumerable<IdentityError> errors)
    {
        var failures = errors.Select(error =>
            new ValidationFailure(ResolvePropertyName(error), error.Description));

        return new ValidationException(failures);
    }

    /// <summary>
    /// Resolves the most appropriate property name for the supplied Identity error.
    /// </summary>
    /// <param name="error">The identity error to classify.</param>
    /// <returns>The related command property name.</returns>
    private static string ResolvePropertyName(IdentityError error)
    {
        var text = $"{error.Code} {error.Description}";

        if (text.Contains("email", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("user", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(RegisterCommand.Email);
        }

        if (text.Contains("first", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(RegisterCommand.FirstName);
        }

        if (text.Contains("last", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(RegisterCommand.LastName);
        }

        return nameof(RegisterCommand.Password);
    }
}
