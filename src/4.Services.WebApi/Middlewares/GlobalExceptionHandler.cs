using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Services.WebApi.Middlewares;

/// <summary>
/// Global exception handler that converts unhandled exceptions into RFC 7807 ProblemDetails responses.
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    /// <summary>
    /// Attempts to handle the current exception and serialize a standardized ProblemDetails payload.
    /// </summary>
    /// <param name="httpContext">Current HTTP context.</param>
    /// <param name="exception">The thrown exception.</param>
    /// <param name="cancellationToken">Cancellation token for async work.</param>
    /// <returns><c>true</c> when the exception is handled.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Unhandled exception for request {Method} {Path}. TraceId: {TraceId}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.TraceIdentifier);

        var problemDetails = CreateProblemDetails(httpContext, exception);
        httpContext.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });
    }

    /// <summary>
    /// Creates a standardized <see cref="ProblemDetails"/> instance based on the exception type.
    /// </summary>
    /// <param name="httpContext">Current HTTP context.</param>
    /// <param name="exception">Thrown exception.</param>
    /// <returns>A properly populated problem details instance.</returns>
    private static ProblemDetails CreateProblemDetails(HttpContext httpContext, Exception exception)
    {
        ProblemDetails problemDetails = exception switch
        {
            ValidationException validationException => CreateValidationProblemDetails(httpContext, validationException),
            NotFoundException notFoundException => CreateProblemDetails(
                httpContext,
                StatusCodes.Status404NotFound,
                "Resource not found",
                notFoundException.Message,
                notFoundException.Code ?? "RESOURCE_NOT_FOUND"),
            DomainException domainException => CreateProblemDetails(
                httpContext,
                StatusCodes.Status400BadRequest,
                "Business rule violation",
                domainException.Message,
                domainException.Code ?? "DOMAIN_RULE_VIOLATION"),
            UnauthorizedAccessException unauthorizedAccessException => CreateProblemDetails(
                httpContext,
                StatusCodes.Status401Unauthorized,
                "Unauthorized",
                unauthorizedAccessException.Message,
                "UNAUTHORIZED"),
            DbUpdateException dbUpdateException
                when TryGetSqlErrorNumber(dbUpdateException.InnerException, out var sqlErrorNumber)
                     && (sqlErrorNumber == 2601 || sqlErrorNumber == 2627) => CreateProblemDetails(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    "Conflict",
                    $"Duplicate key violation detected (SQL {sqlErrorNumber}).",
                    "DUPLICATE_KEY"),
            _ => CreateProblemDetails(
                httpContext,
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "An unexpected error occurred. Please try again later.",
                "INTERNAL_SERVER_ERROR")
        };

        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
        problemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;

        return problemDetails;
    }

    /// <summary>
    /// Creates a <see cref="ValidationProblemDetails"/> payload for FluentValidation errors.
    /// </summary>
    /// <param name="httpContext">Current HTTP context.</param>
    /// <param name="exception">Validation exception containing grouped field errors.</param>
    /// <returns>A validation-oriented problem details instance.</returns>
    private static ValidationProblemDetails CreateValidationProblemDetails(HttpContext httpContext, ValidationException exception)
    {
        var problemDetails = new ValidationProblemDetails(exception.Errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failure",
            Detail = "One or more validation errors occurred.",
            Type = "https://httpstatuses.com/400",
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = "VALIDATION_FAILED";
        return problemDetails;
    }

    /// <summary>
    /// Creates a generic <see cref="ProblemDetails"/> payload.
    /// </summary>
    /// <param name="httpContext">Current HTTP context.</param>
    /// <param name="statusCode">HTTP status code.</param>
    /// <param name="title">Short error title.</param>
    /// <param name="detail">Detailed error message.</param>
    /// <param name="code">Machine-readable application error code.</param>
    /// <returns>A populated problem details instance.</returns>
    private static ProblemDetails CreateProblemDetails(
        HttpContext httpContext,
        int statusCode,
        string title,
        string detail,
        string code)
    {
        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.com/{statusCode}",
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["code"] = code;
        return problemDetails;
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
