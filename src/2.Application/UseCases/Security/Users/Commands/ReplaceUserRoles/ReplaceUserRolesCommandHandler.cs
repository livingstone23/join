using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Users.Commands.ReplaceUserRoles;

/// <summary>
/// Handles the replacement of all roles assigned to a user.
/// </summary>
/// <param name="userManager">Identity manager used to resolve the target user and mutate role assignments.</param>
/// <param name="roleManager">Identity manager used to validate the requested roles.</param>
public sealed class ReplaceUserRolesCommandHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager)
    : IRequestHandler<ReplaceUserRolesCommand, Response<UserWithRolesDto>>
{
    /// <summary>
    /// Replaces the complete role set for the requested user.
    /// </summary>
    /// <param name="request">The update payload.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A standardized response containing the updated user projection.</returns>
    public async Task<Response<UserWithRolesDto>> Handle(
        ReplaceUserRolesCommand request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return new Response<UserWithRolesDto>
            {
                IsSuccess = false,
                Message = "User not found."
            };
        }

        var requestedRoles = (request.Roles ?? Array.Empty<string>())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var normalizedRequestedRoles = requestedRoles
            .Select(role => role.ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingRoles = normalizedRequestedRoles.Count == 0
            ? []
            : roleManager.Roles
                .Where(role => role.Name != null && normalizedRequestedRoles.Contains(role.NormalizedName!))
                .Select(role => role.Name!)
                .ToList();

        var missingRoles = requestedRoles
            .Except(existingRoles, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingRoles.Length > 0)
        {
            return new Response<UserWithRolesDto>
            {
                IsSuccess = false,
                Message = "One or more roles do not exist.",
                Errors = missingRoles.Select(role => $"Role '{role}' does not exist.")
            };
        }

        var currentRoles = await userManager.GetRolesAsync(user);

        var rolesToAdd = existingRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        if (rolesToAdd.Length > 0)
        {
            var addResult = await userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                return new Response<UserWithRolesDto>
                {
                    IsSuccess = false,
                    Message = "Unable to add one or more roles.",
                    Errors = addResult.Errors.Select(error => error.Description)
                };
            }
        }

        var rolesToRemove = currentRoles.Except(existingRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        if (rolesToRemove.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                return new Response<UserWithRolesDto>
                {
                    IsSuccess = false,
                    Message = "Unable to remove one or more roles.",
                    Errors = removeResult.Errors.Select(error => error.Description)
                };
            }
        }

        var updatedRoles = await userManager.GetRolesAsync(user);

        return new Response<UserWithRolesDto>
        {
            Data = new UserWithRolesDto
            {
                Id = user.Id,
                UserName = user.UserName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                IsActive = user.IsActive,
                Roles = updatedRoles.OrderBy(role => role).ToArray()
            },
            IsSuccess = true,
            Message = "User roles updated successfully."
        };
    }
}
