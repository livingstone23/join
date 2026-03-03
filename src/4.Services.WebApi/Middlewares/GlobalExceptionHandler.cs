


using JOIN.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;



namespace JOIN.Services.WebApi.Middlewares;



/// <summary>
/// Global exception handler intercepting all unhandled exceptions in the HTTP pipeline.
/// Translates domain and application exceptions into standardized RFC 7807 ProblemDetails responses.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{

    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "An unhandled exception occurred: {Message}", exception.Message);

        var problemDetails = new ProblemDetails
        {
            Instance = httpContext.Request.Path
        };

        // Pattern matching to handle specific custom exceptions
        if (exception is ValidationException validationException)
        {
            problemDetails.Title = "Validation Failed";
            problemDetails.Status = StatusCodes.Status400BadRequest;
            problemDetails.Detail = "One or more validation errors occurred. Please check the provided data.";
            problemDetails.Extensions["errors"] = validationException.Errors;
        }
        else
        {
            // Fallback for generic/unexpected server errors
            problemDetails.Title = "Internal Server Error";
            problemDetails.Status = StatusCodes.Status500InternalServerError;
            problemDetails.Detail = "An unexpected fault happened. Try again later.";
        }

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        // Return true to signal that this exception has been handled and shouldn't propagate further.
        return true;
        
    }

}
