


namespace JOIN.Application.Interface;



/// <summary>
/// Provides access to the currently authenticated user's context.
/// Essential for auditing, authorization, and multi-tenant data filtering
/// without coupling the Application or Domain layers to HTTP specifics.
/// </summary>
public interface ICurrentUserService
{

    /// <summary>
    /// Gets the unique identifier (GUID) of the currently authenticated user.
    /// Returns null if the request is not authenticated (e.g., anonymous endpoints or background jobs).
    /// </summary>
    string? UserId { get; }


    /// <summary>
    /// Gets the unique identifier (GUID) of the company associated with the currently authenticated user.
    /// This is essential for multi-tenant data filtering and ensuring that users can only access data within their own company context.
    /// </summary>
    /// <value></value>
    Guid CompanyId { get; }


    /// <summary>
    /// Gets a value indicating whether the current execution context is associated with an authenticated user.
    /// </summary>
    bool IsAuthenticated { get; }
    
}