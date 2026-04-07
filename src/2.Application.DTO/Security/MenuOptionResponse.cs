namespace JOIN.Application.DTO.Security;

/// <summary>
/// Represents one node in the hierarchical sidebar menu resolved for the authenticated user.
/// Each node carries both its visual navigation metadata and the effective action permissions that should be exposed to the client UI.
/// </summary>
public sealed record MenuOptionResponse
{
    /// <summary>
    /// Gets the unique identifier of the system option represented by the sidebar node.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the display name that should be rendered in the sidebar menu for this option.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional icon key or CSS class associated with the menu option.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Gets the logical controller or resource name that the option targets when the node is actionable.
    /// Parent grouping nodes can expose <see langword="null"/> for this value.
    /// </summary>
    public string? ControllerName { get; init; }

    /// <summary>
    /// Gets the identifier of the parent menu option when the current node is nested under another option.
    /// </summary>
    public Guid? ParentId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the current user can read or access the resource represented by this menu node.
    /// </summary>
    public bool CanRead { get; init; }

    /// <summary>
    /// Gets a value indicating whether the current user can create records from the resource represented by this menu node.
    /// </summary>
    public bool CanCreate { get; init; }

    /// <summary>
    /// Gets a value indicating whether the current user can update records from the resource represented by this menu node.
    /// </summary>
    public bool CanUpdate { get; init; }

    /// <summary>
    /// Gets a value indicating whether the current user can delete records from the resource represented by this menu node.
    /// </summary>
    public bool CanDelete { get; init; }

    /// <summary>
    /// Gets the child nodes nested under the current menu option.
    /// This collection is empty when the option is a leaf node in the sidebar tree.
    /// </summary>
    public List<MenuOptionResponse> Children { get; init; } = [];
}
