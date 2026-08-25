namespace JOIN.Application.DTO.Security;

/// <summary>
/// Request payload for <c>PUT /Users/roles/bulk</c>. SPEC 28 / F6, item 20. The
/// endpoint applies a <b>delta</b> (additions + removals) to a batch of users in a
/// single transaction. <see cref="UserIds"/> is the batch; <see cref="AddRoleIds"/>
/// and <see cref="RemoveRoleIds"/> are role-ids (Guid), unlike the single-user
/// endpoint that takes role names. Topes (<c>200</c> users, <c>40</c> combined roles)
/// live in <c>BulkUpdateUserRolesCommandValidator</c>.
/// </summary>
/// <param name="UserIds">Users whose role set will be mutated.</param>
/// <param name="AddRoleIds">Roles to add to every user in the batch that does not already have them.</param>
/// <param name="RemoveRoleIds">Roles to remove from every user in the batch that currently has them.</param>
public sealed record BulkUpdateUserRolesRequestDto(
    IReadOnlyList<Guid> UserIds,
    IReadOnlyList<Guid> AddRoleIds,
    IReadOnlyList<Guid> RemoveRoleIds);