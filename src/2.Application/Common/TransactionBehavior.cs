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
/// </summary>
/// <typeparam name="TRequest">The incoming request type.</typeparam>
/// <typeparam name="TResponse">The outgoing response type.</typeparam>
public class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Executes the next pipeline step inside an explicit EF Core transaction when the
    /// request implements <see cref="ITransactionalCommand{TResponse}"/>. On exception,
    /// rolls back the transaction and rethrows the original exception unchanged.
    /// </summary>
    /// <param name="request">Incoming MediatR request.</param>
    /// <param name="next">Delegate for the next pipeline component (validation/handler/etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The handler response when the request is transactional and completes successfully.</returns>
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
            await unitOfWork.CommitAsync(cancellationToken);
            return response;
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
