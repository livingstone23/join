using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JOIN.Application.Common;

/// <summary>
/// MediatR pipeline behavior that logs any genuinely unexpected exception thrown by the
/// downstream pipeline (handler, validation, transaction, etc.). Business-expected
/// exceptions — <see cref="ValidationException"/>, <see cref="NotFoundException"/>,
/// <see cref="DomainException"/> — are rethrown untouched but NOT logged here because
/// <c>GlobalExceptionHandler</c> already maps them to standard HTTP responses and
/// logging them would flood the error channel with non-incidents.
///
/// The original exception is always rethrown via <c>throw;</c> — never wrapped,
/// never swallowed.
/// </summary>
/// <typeparam name="TRequest">The incoming request type.</typeparam>
/// <typeparam name="TResponse">The outgoing response type.</typeparam>
public class UnhandledExceptionBehavior<TRequest, TResponse>(
    ILogger<TRequest> logger,
    ICurrentUserService currentUserService)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<TRequest> _logger = logger;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    /// <summary>
    /// Executes the next pipeline step and logs the exception when it is not one
    /// of the business-expected types.
    /// </summary>
    /// <param name="request">Incoming MediatR request.</param>
    /// <param name="next">Delegate for the next pipeline component.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The handler response when no exception is thrown.</returns>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex) when (ex is not ValidationException
                                       and not NotFoundException
                                       and not DomainException)
        {
            var requestName = typeof(TRequest).Name;

            _logger.LogError(
                ex,
                "JOIN Unhandled Exception: {Name} {@UserId}",
                requestName,
                _currentUserService.UserId);

            throw;
        }
    }
}