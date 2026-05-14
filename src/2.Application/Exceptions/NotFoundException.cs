namespace JOIN.Application.Exceptions;



/// <summary>
/// Represents an error that occurs when an application use case cannot find a requested resource.
/// </summary>
public class NotFoundException : Exception
{
    

    /// <summary>
    /// Gets the resource name associated with the failed lookup.
    /// </summary>
    public string ResourceName { get; }


    /// <summary>
    /// Gets the key or identifier used in the failed lookup.
    /// </summary>
    public object? Key { get; }


    /// <summary>
    /// Gets the optional machine-readable error code.
    /// </summary>
    public string? Code { get; }


    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundException"/> class.
    /// </summary>
    /// <param name="resourceName">Resource name.</param>
    /// <param name="key">Lookup key.</param>
    public NotFoundException(string resourceName, object? key)
        : base($"{resourceName} with key '{key}' was not found.")
    {
        ResourceName = resourceName;
        Key = key;
        Code = $"{resourceName.ToUpperInvariant()}_NOT_FOUND";
    }


    /// <summary>
    /// Initializes a new instance of the <see cref="NotFoundException"/> class.
    /// </summary>
    /// <param name="resourceName">Resource name.</param>
    /// <param name="key">Lookup key.</param>
    /// <param name="message">Custom error message.</param>
    public NotFoundException(string resourceName, object? key, string message)
        : base(message)
    {
        ResourceName = resourceName;
        Key = key;
        Code = $"{resourceName.ToUpperInvariant()}_NOT_FOUND";
    }


}
