namespace JOIN.Application.DTO.Security;

/// <summary>
/// Per-user outcome of <c>PUT /Users/roles/bulk</c>. SPEC 28 / F6, item 20. The
/// enum is serialized as its numeric value; the API contract documents
/// <c>Updated</c> / <c>NoChange</c> / <c>UserNotFound</c> as the three legal
/// outcomes the operator can observe in the response items array.
/// </summary>
public enum BulkRoleOutcome
{
    /// <summary>At least one role was added or removed.</summary>
    Updated = 1,

    /// <summary>The user already had exactly the resulting set; nothing was written.</summary>
    NoChange = 2,

    /// <summary>The user has no active membership in the caller's tenant.</summary>
    UserNotFound = 3
}