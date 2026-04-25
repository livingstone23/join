namespace JOIN.Services.WebApi.Services.RateLimiting;

/// <summary>
/// Represents the configuration for API rate limiting policies.
/// </summary>
public sealed class JoinRateLimitingOptions
{
    /// <summary>
    /// Gets or sets whether strict rate limiting should be enforced on critical endpoints (login, refresh, password recovery).
    /// When false (default), only the global policy applies.
    /// When true, the Strict policy (5 req/min) is applied to protected auth endpoints.
    /// </summary>
    public bool EnableStrictSecurity { get; set; } = false;

    /// <summary>
    /// Gets or sets the global policy applied to the API by default.
    /// </summary>
    public JoinRateLimitPolicyOptions Global { get; set; } = new();

    /// <summary>
    /// Gets or sets the strict policy reserved for critical endpoints.
    /// </summary>
    public JoinRateLimitPolicyOptions Strict { get; set; } = new()
    {
        PermitLimit = 5,
        WindowSeconds = 60,
        QueueLimit = 0,
        QueueProcessingOrder = "OldestFirst"
    };

    /// <summary>
    /// Gets or sets the RFC7807 payload customization for 429 responses.
    /// </summary>
    public JoinRateLimitRejectionOptions Rejection { get; set; } = new();
}

/// <summary>
/// Represents options for a fixed-window rate limiting policy.
/// </summary>
public sealed class JoinRateLimitPolicyOptions
{
    /// <summary>
    /// Gets or sets the maximum number of requests allowed in one window.
    /// </summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    /// Gets or sets the window size in seconds.
    /// </summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Gets or sets the queue size for excess requests.
    /// </summary>
    public int QueueLimit { get; set; } = 0;

    /// <summary>
    /// Gets or sets queue processing order. Supported values: OldestFirst, NewestFirst.
    /// </summary>
    public string QueueProcessingOrder { get; set; } = "OldestFirst";
}

/// <summary>
/// Represents RFC7807 fields used when a request is rejected by rate limiting.
/// </summary>
public sealed class JoinRateLimitRejectionOptions
{
    /// <summary>
    /// Gets or sets the problem type URI.
    /// </summary>
    public string Type { get; set; } = "https://httpstatuses.com/429";

    /// <summary>
    /// Gets or sets the problem title.
    /// </summary>
    public string Title { get; set; } = "Too Many Requests";

    /// <summary>
    /// Gets or sets the base problem detail message.
    /// </summary>
    public string Detail { get; set; } = "You have exceeded the request limit. Please try again later.";
}
