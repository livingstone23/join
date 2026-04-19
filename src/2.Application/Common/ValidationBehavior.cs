
using FluentValidation;
using MediatR;

namespace JOIN.Application.Common;

/// <summary>
/// MediatR pipeline behavior that automatically runs FluentValidation rules before executing a request handler.
/// </summary>
/// <typeparam name="TRequest">The incoming request type.</typeparam>
/// <typeparam name="TResponse">The outgoing response type.</typeparam>
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Validates the incoming request and throws a standardized application validation exception when failures exist.
    /// </summary>
    /// <param name="request">Incoming MediatR request.</param>
    /// <param name="next">Delegate for the next pipeline component.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The handler response when validation succeeds.</returns>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);
        var validationResults = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToList();

        if (failures.Count != 0)
        {
            throw new ValidationException(failures);
        }

        return await next();
    }
}