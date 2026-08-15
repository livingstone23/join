using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Security;

/// <summary>
/// Maps <c>AccountController</c> business error codes to HTTP status codes per SPEC 26 / F11.
/// Codes that aren't explicit in the spec table fall back to 400 Bad Request — a deliberate
/// fail-closed choice for unknown business failures.
/// </summary>
internal static class AccountErrorCodes
{
    private static readonly Dictionary<string, int> Map = new(StringComparer.Ordinal)
    {
        ["SESSION_NOT_FOUND"] = StatusCodes.Status404NotFound,
        ["USER_NOT_FOUND"] = StatusCodes.Status404NotFound,
        ["ACCOUNT_NOT_FOUND"] = StatusCodes.Status404NotFound,

        ["CANNOT_REVOKE_CURRENT"] = StatusCodes.Status400BadRequest,
        ["TENANT_REQUIRED"] = StatusCodes.Status400BadRequest,
        ["MFA_NOT_CONFIGURED"] = StatusCodes.Status400BadRequest,
        ["MFA_INVALID_CODE"] = StatusCodes.Status400BadRequest,
        ["EMAIL_CHANGE_FAILED"] = StatusCodes.Status400BadRequest,
        ["PHONE_INVALID_FORMAT"] = StatusCodes.Status400BadRequest,
        ["PHONE_CODE_EXPIRED"] = StatusCodes.Status400BadRequest,
        ["PHONE_CODE_INVALID"] = StatusCodes.Status400BadRequest,

        // 429 Too Many Requests reserved for the SMS-code lock path.
        ["PHONE_CODE_LOCKED"] = StatusCodes.Status429TooManyRequests
    };

    /// <summary>
    /// Resolves the HTTP status for a code. Unknown codes fall back to 400.
    /// </summary>
    public static int Resolve(string? code) =>
        code is not null && Map.TryGetValue(code, out var status)
            ? status
            : StatusCodes.Status400BadRequest;

    /// <summary>
    /// Returns a ProblemDetails-style ObjectResult carrying the supplied error code,
    /// message, and detailed errors so the existing <c>Response&lt;T&gt;</c> payload shape
    /// survives end-to-end.
    /// </summary>
    public static ObjectResult ToResult<T>(JOIN.Application.Common.Response<T> response)
    {
        var status = Resolve(response.Message);
        return new ObjectResult(response)
        {
            StatusCode = status
        };
    }
}
