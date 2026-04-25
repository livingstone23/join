using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace JOIN.Services.WebApi.Services.RateLimiting;

/// <summary>
/// Registers rate limiting and forwarded headers for JOIN Web API.
/// </summary>
public static class RateLimitingServiceCollectionExtensions
{
    /// <summary>
    /// Adds rate limiting services and policies configured from appsettings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for fluent chaining.</returns>
    public static IServiceCollection AddJoinRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("RateLimiting");
        services.Configure<JoinRateLimitingOptions>(section);

        var configured = section.Get<JoinRateLimitingOptions>() ?? new JoinRateLimitingOptions();
        var global = Normalize(configured.Global);
        var strict = Normalize(configured.Strict);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 2;
        });

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = static async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                if (response.HasStarted)
                {
                    return;
                }

                var configuredOptions = context.HttpContext.RequestServices
                    .GetRequiredService<IOptions<JoinRateLimitingOptions>>()
                    .Value;

                var detail = configuredOptions.Rejection.Detail;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString();
                    detail = $"{detail} Retry after {Math.Ceiling(retryAfter.TotalSeconds)} seconds.";
                }

                var problem = new ProblemDetails
                {
                    Type = configuredOptions.Rejection.Type,
                    Title = configuredOptions.Rejection.Title,
                    Status = StatusCodes.Status429TooManyRequests,
                    Detail = detail,
                    Instance = context.HttpContext.Request.Path
                };

                problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                problem.Extensions["timestamp"] = DateTimeOffset.UtcNow;

                response.StatusCode = StatusCodes.Status429TooManyRequests;
                response.ContentType = "application/problem+json";
                await response.WriteAsync(JsonSerializer.Serialize(problem), cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var key = BuildPartitionKey(httpContext, "global");
                return RateLimitPartition.GetFixedWindowLimiter(
                    key,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = global.PermitLimit,
                        Window = TimeSpan.FromSeconds(global.WindowSeconds),
                        QueueLimit = global.QueueLimit,
                        QueueProcessingOrder = ResolveQueueOrder(global.QueueProcessingOrder),
                        AutoReplenishment = true
                    });
            });

            options.AddPolicy("Strict", httpContext =>
            {
                var key = BuildPartitionKey(httpContext, "strict");
                return RateLimitPartition.GetFixedWindowLimiter(
                    key,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = strict.PermitLimit,
                        Window = TimeSpan.FromSeconds(strict.WindowSeconds),
                        QueueLimit = strict.QueueLimit,
                        QueueProcessingOrder = ResolveQueueOrder(strict.QueueProcessingOrder),
                        AutoReplenishment = true
                    });
            });
        });

        return services;
    }

    private static JoinRateLimitPolicyOptions Normalize(JoinRateLimitPolicyOptions policy)
    {
        policy.PermitLimit = Math.Max(1, policy.PermitLimit);
        policy.WindowSeconds = Math.Max(1, policy.WindowSeconds);
        policy.QueueLimit = Math.Max(0, policy.QueueLimit);

        if (!string.Equals(policy.QueueProcessingOrder, "OldestFirst", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(policy.QueueProcessingOrder, "NewestFirst", StringComparison.OrdinalIgnoreCase))
        {
            policy.QueueProcessingOrder = "OldestFirst";
        }

        return policy;
    }

    private static QueueProcessingOrder ResolveQueueOrder(string configuredValue)
    {
        return string.Equals(configuredValue, "NewestFirst", StringComparison.OrdinalIgnoreCase)
            ? QueueProcessingOrder.NewestFirst
            : QueueProcessingOrder.OldestFirst;
    }

    private static string BuildPartitionKey(HttpContext context, string policyName)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"{policyName}:{ip}";
    }
}
