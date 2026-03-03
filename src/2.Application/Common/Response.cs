


namespace JOIN.Application.Common;



/// <summary>
/// Generic wrapper for all API responses.
/// Ensures a consistent payload structure across the entire ecosystem.
/// </summary>
/// <typeparam name="T">The type of the data payload.</typeparam>
public class Response<T>
{
    
    /// <summary> Gets or sets the data payload. </summary>
    public T? Data { get; set; }

    /// <summary> Indicates whether the operation was successful. </summary>
    public bool IsSuccess { get; set; }

    /// <summary> Provides a human-readable success or error message. </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary> Collection of validation errors, if any. </summary>
    public IEnumerable<string>? Errors { get; set; }

}