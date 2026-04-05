


using JOIN.Application.Common;
using System.Linq;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;



namespace JOIN.Services.WebApi.Middlewares;



/// <summary>
/// Global exception handler intercepting all unhandled exceptions in the HTTP pipeline.
/// Translates domain and application exceptions into standardized Response payloads.
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{

    private readonly ILogger<GlobalExceptionHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalExceptionHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger used to record unhandled exceptions.</param>
    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Attempts to handle unhandled exceptions and convert them into RFC 7807 responses.
    /// </summary>
    /// <param name="httpContext">Current HTTP context.</param>
    /// <param name="exception">The thrown exception.</param>
    /// <param name="cancellationToken">Cancellation token for async operation.</param>
    /// <returns><c>true</c> when the exception was handled.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "An unhandled exception occurred: {Message}", exception.Message);

        // Pattern matching to handle specific custom exceptions
        if (exception is ValidationException validationException)
        {
            var validationErrors = validationException.Errors
                .SelectMany(kvp => kvp.Value.Select(error => $"{kvp.Key}: {error}"))
                .ToArray();

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(
                Response<object>.Error(
                    "VALIDATION_FAILED",
                    validationErrors),
                cancellationToken);

            return true;
        }
        else if (exception is UnauthorizedAccessException unauthorizedAccessException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await httpContext.Response.WriteAsJsonAsync(
                Response<object>.Error(
                    "UNAUTHORIZED",
                    [unauthorizedAccessException.Message]),
                cancellationToken);

            return true;
        }
        else if (exception is DbUpdateException dbUpdateException
                 && TryGetSqlErrorNumber(dbUpdateException.InnerException, out var sqlErrorNumber)
                 && (sqlErrorNumber == 2601 || sqlErrorNumber == 2627))
        {
            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            await httpContext.Response.WriteAsJsonAsync(
                Response<object>.Error(
                    "CUSTOMER_ALREADY_EXISTS",
                    [$"Duplicate key violation detected (SQL {sqlErrorNumber})."]),
                cancellationToken);

            return true;
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            Response<object>.Error(
                "INTERNAL_SERVER_ERROR",
                ["An unexpected fault happened. Try again later."]),
            cancellationToken);

        // Return true to signal that this exception has been handled and shouldn't propagate further.
        return true;
        
    }

    /// <summary>
    /// Tries to extract a SQL Server error number from an exception without coupling to a specific SqlClient package.
    /// </summary>
    /// <param name="exception">Potential SQL exception instance.</param>
    /// <param name="errorNumber">Extracted SQL error number.</param>
    /// <returns><c>true</c> if a SQL error number could be extracted; otherwise, <c>false</c>.</returns>
    private static bool TryGetSqlErrorNumber(Exception? exception, out int errorNumber)
    {
        errorNumber = 0;

        if (exception is null)
        {
            return false;
        }

        var type = exception.GetType();
        var fullName = type.FullName;
        if (fullName is not "Microsoft.Data.SqlClient.SqlException" and not "System.Data.SqlClient.SqlException")
        {
            return false;
        }

        var numberProperty = type.GetProperty("Number");
        if (numberProperty?.PropertyType != typeof(int))
        {
            return false;
        }

        var value = numberProperty.GetValue(exception);
        if (value is not int sqlNumber)
        {
            return false;
        }

        errorNumber = sqlNumber;
        return true;
    }

}
