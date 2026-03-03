


using System.ComponentModel.DataAnnotations;
using FluentValidation;
using MediatR;



namespace JOIN.Application.Common;



/// <summary>
/// MediatR pipeline behavior that automatically runs FluentValidation rules 
/// before executing the respective Command or Query Handler.
/// </summary>
/// <typeparam name="TRequest">The incoming request type.</typeparam>
/// <typeparam name="TResponse">The outgoing response type.</typeparam>
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : class, new() // Enforces that TResponse can be instantiated (like Response<T>)
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    /// <param name="validators">Injected list of validators for the current request type.</param>
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_validators.Any())
        {
            // If no validators exist for this request, proceed to the handler
            return await next();
        }

        var context = new ValidationContext<TRequest>(request);

        // Run all validators asynchronously
        var validationResults = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        // Extract any failures
        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count != 0)
        {
            // If it's our standard Response wrapper, populate the errors and return gracefully
            var responseType = typeof(TResponse);
            if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Response<>))
            {
                var errorResponse = new TResponse();
                
                // Use reflection to set standard properties defined in Response<T>
                var isSuccessProp = responseType.GetProperty("IsSuccess");
                var messageProp = responseType.GetProperty("Message");
                var errorsProp = responseType.GetProperty("Errors");

                isSuccessProp?.SetValue(errorResponse, false);
                messageProp?.SetValue(errorResponse, "Validation failed. Please check the provided data.");
                errorsProp?.SetValue(errorResponse, failures.Select(f => f.ErrorMessage).Distinct().ToList());

                return errorResponse;
            }

            // Fallback: throw OUR CUSTOM validation exception (Clean Architecture approach)
            throw new JOIN.Application.Common.ValidationException(failures);
        }

        // Proceed to the actual handler if validation passes
        return await next();

    }

}