using System.Reflection;
using JOIN.Application.Interface.Persistence;
using MediatR;

namespace JOIN.Application.Common;

/// <summary>
/// MediatR pipeline behavior that wraps every <see cref="ITransactionalCommand{TResponse}"/>
/// in an explicit EF Core transaction (begin / commit / rollback) using <see cref="IUnitOfWork"/>.
///
/// Runtime opt-in via interface check; requests that do NOT implement
/// <see cref="ITransactionalCommand{TResponse}"/> are passed through to <c>next()</c>
/// untouched and never touch <see cref="IUnitOfWork"/>. This deliberately mirrors
/// how the behavior was specified, instead of a constrained DI registration that
/// would prevent construction for non-qualifying requests.
///
/// Business-failure rollback: when the inner handler returns a <see cref="Response{T}"/>
/// with <c>IsSuccess == false</c>, the transaction is rolled back instead of committed,
/// so rows written before the failure are not persisted. Detection is done by reflection
/// on a <c>bool IsSuccess</c> property because <see cref="Response{T}"/> has no common
/// base or interface. Responses that do not expose <c>IsSuccess</c> keep the previous
/// behavior (always commit).
/// </summary>
/// <typeparam name="TRequest">The incoming request type.</typeparam>
/// <typeparam name="TResponse">The outgoing response type.</typeparam>
public class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    // Cached per closed TResponse so the reflection cost is paid once per generic
    // instantiation rather than on every request.
    private static readonly PropertyInfo? IsSuccessProperty = typeof(TResponse).GetProperty(
        nameof(Response<object>.IsSuccess),
        BindingFlags.Public | BindingFlags.Instance);

    /// <summary>
    /// Executes the next pipeline step inside an explicit EF Core transaction when the
    /// request implements <see cref="ITransactionalCommand{TResponse}"/>. On business
    /// failure (response with <c>IsSuccess == false</c>) or exception, rolls back the
    /// transaction. On success, commits and returns the response unchanged.
    /// </summary>
    /// <param name="request">Incoming MediatR request.</param>
    /// <param name="next">Delegate for the next pipeline component (validation/handler/etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The handler response when the request is transactional.</returns>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not ITransactionalCommand<TResponse>)
        {
            return await next();
        }

        await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            var response = await next();

            if (IsFailedResponse(response))
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return response;
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="response"/> exposes a <c>bool IsSuccess</c>
    /// property whose value is <c>false</c>. Returns <c>false</c> for any other shape so
    /// non-<see cref="Response{T}"/> responses keep the legacy commit-on-exit behavior.
    /// </summary>
    /// <param name="response">The response returned by the next pipeline step.</param>
    private static bool IsFailedResponse(TResponse response)
    {
        if (response is null)
        {
            return false;
        }

        if (IsSuccessProperty is null || IsSuccessProperty.PropertyType != typeof(bool))
        {
            return false;
        }

        return IsSuccessProperty.GetValue(response) is false;
    }
}
