namespace JOIN.Application.DTO.Security;

/// <summary>
/// Payload for a single permission row inside the bulk upsert request.
/// The 7 <c>Can*</c> flags mirror the entity shape introduced by SPEC 22
/// (the same flags PermissionService reads when authorizing).
/// </summary>
public sealed record UpsertRoleSystemOptionItemDto(
    Guid SystemOptionId,
    bool CanRead,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete,
    bool CanDownload,
    bool CanExport,
    bool CanExecute);

/// <summary>
/// Request payload for <c>PUT /api/v1/RoleSystemOptions/bulk</c>.
/// <c>Items</c> represents the desired final set of <c>RoleSystemOption</c>
/// rows for <c>(roleId, currentUser.CompanyId)</c>.
/// Rows present in the DB but absent from <c>Items</c> get soft-deleted;
/// rows present in <c>Items</c> but absent from the DB get inserted;
/// rows present in both get their flags updated.
/// </summary>
public sealed record BulkUpsertRoleSystemOptionsRequest(
    Guid RoleId,
    IReadOnlyList<UpsertRoleSystemOptionItemDto> Items);

/// <summary>
/// Result of <c>PUT /api/v1/RoleSystemOptions/bulk</c>. The lists carry the
/// resulting <c>RoleSystemOption.Id</c> values (not <c>SystemOptionId</c>),
/// so the client can correlate with audit/log entries.
/// </summary>
/// <param name="Created">IDs of rows that did not exist and were inserted.</param>
/// <param name="Updated">IDs of rows that already existed and had their flags overwritten.</param>
/// <param name="Removed">IDs of rows that existed and were soft-deleted because they were absent from the request.</param>
public sealed record BulkUpsertRoleSystemOptionsResult(
    IReadOnlyList<Guid> Created,
    IReadOnlyList<Guid> Updated,
    IReadOnlyList<Guid> Removed);