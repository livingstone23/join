namespace JOIN.Domain.Exceptions;

/// <summary>
/// Represents a business rule violation raised by the domain model.
/// </summary>
public class DomainException : Exception
{
    /// <summary>
    /// Gets the optional business error code associated with the exception.
    /// </summary>
    public string? Code { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainException"/> class.
    /// </summary>
    /// <param name="message">Business error message.</param>
    public DomainException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainException"/> class.
    /// </summary>
    /// <param name="code">Machine-readable business error code.</param>
    /// <param name="message">Business error message.</param>
    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainException"/> class.
    /// </summary>
    /// <param name="message">Business error message.</param>
    /// <param name="innerException">Inner exception.</param>
    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
