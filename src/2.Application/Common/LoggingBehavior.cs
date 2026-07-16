using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JOIN.Application.Common;

/// <summary>
/// MediatR pipeline behavior that emits a structured information log at the start
/// and at the successful end of every request flowing through the pipeline.
///
/// No <c>try/catch</c> is used: when the downstream throws, the "Finished" log
/// is simply skipped and the exception propagates untouched — this behavior
/// never alters control flow, only adds observability.
///
/// A <c>Response&lt;T&gt;.IsSuccess = false</c> from a successful handler is still
/// logged as "Finished" because this behavior cannot read <c>IsSuccess</c> on a
/// generic <c>TResponse</c> without reflection; the trade-off is documented in the spec.
/// </summary>
/// <typeparam name="TRequest">The incoming request type.</typeparam>
/// <typeparam name="TResponse">The outgoing response type.</typeparam>
public class LoggingBehavior<TRequest, TResponse>(
    ILogger<TRequest> logger,
    ICurrentUserService currentUserService)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<TRequest> _logger = logger;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    /// <summary>
    /// Logs "JOIN Request Started" before invoking <paramref name="next"/> and
    /// "JOIN Request Finished" only if <paramref name="next"/> returns successfully.
    /// </summary>
    /// <param name="request">Incoming MediatR request.</param>
    /// <param name="next">Delegate for the next pipeline component.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The handler response.</returns>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var userId = _currentUserService.UserId;

        _logger.LogInformation(
            "JOIN Request Started: {Name} {@UserId}",
            requestName,
            userId);

        var response = await next();

        _logger.LogInformation(
            "JOIN Request Finished: {Name} {@UserId}",
            requestName,
            userId);

        return response;
    }
}