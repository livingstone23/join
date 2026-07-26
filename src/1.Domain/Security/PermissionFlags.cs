namespace JOIN.Domain.Security;

/// <summary>
/// Permission flags exposed by <see cref="RoleSystemOption"/> and <see cref="SystemOption"/>
/// for runtime authorization decisions. Mirrors the seven boolean columns on the
/// role/option matrices (CanRead / CanCreate / CanUpdate / CanDelete / CanDownload / CanExport / CanExecute).
/// </summary>
[Flags]
public enum PermissionFlags
{
    None        = 0,
    CanRead     = 1 << 0,
    CanCreate   = 1 << 1,
    CanUpdate   = 1 << 2,
    CanDelete   = 1 << 3,
    CanDownload = 1 << 4,
    CanExport   = 1 << 5,
    CanExecute  = 1 << 6,
}
