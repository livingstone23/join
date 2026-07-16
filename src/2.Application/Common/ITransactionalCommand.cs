using MediatR;

namespace JOIN.Application.Common;

/// <summary>
/// Marker interface for Commands that must run inside an explicit EF Core
/// transaction managed by <c>TransactionBehavior</c>. Handlers are not modified:
/// they keep calling <c>SaveChangesAsync</c> / <c>SaveAsync</c> themselves; the
/// pipeline is responsible only for begin / commit / rollback around the handler.
///
/// Queries and Commands that touch external I/O (email, HTTP, queues) MUST NOT
/// implement this interface — their boundary cannot be a single DB transaction.
/// </summary>
public interface ITransactionalCommand<TResponse> : IRequest<TResponse>
{
}
