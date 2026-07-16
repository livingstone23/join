using System.Diagnostics;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JOIN.Application.Common;

/// <summary>
/// MediatR pipeline behavior that measures the elapsed time of every <see cref="IRequest{TResponse}"/>
/// using a <see cref="Stopwatch"/>. When the elapsed time exceeds
/// <see cref="PerformanceSettings.ThresholdMilliseconds"/>, a structured warning is emitted.
///
/// Applies to 100% of requests (Commands and Queries) — no marker interface required.
/// The measurement is wrapped in a try/finally so the threshold is evaluated even when
/// the downstream pipeline throws; the original exception is rethrown untouched.
/// </summary>
/// <typeparam name="TRequest">The incoming request type.</typeparam>
/// <typeparam name="TResponse">The outgoing response type.</typeparam>
public class PerformanceBehavior<TRequest, TResponse>(
    ILogger<TRequest> logger,
    IOptions<PerformanceSettings> options,
    ICurrentUserService currentUserService)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<TRequest> _logger = logger;
    private readonly PerformanceSettings _settings = options.Value;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    /// <summary>
    /// Measures the elapsed time of the next pipeline step and emits a warning
    /// when the configured threshold is exceeded.
    /// </summary>
    /// <param name="request">Incoming MediatR request.</param>
    /// <param name="next">Delegate for the next pipeline component.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The handler response.</returns>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestName = typeof(TRequest).Name;

        try
        {
            return await next();
        }
        finally
        {
            stopwatch.Stop();
            var elapsedMilliseconds = stopwatch.ElapsedMilliseconds;

            if (elapsedMilliseconds > _settings.ThresholdMilliseconds)
            {
                _logger.LogWarning(
                    "JOIN Long Running Request: {Name} ({ElapsedMilliseconds} ms) {@UserId}",
                    requestName,
                    elapsedMilliseconds,
                    _currentUserService.UserId);
            }
        }
    }
}