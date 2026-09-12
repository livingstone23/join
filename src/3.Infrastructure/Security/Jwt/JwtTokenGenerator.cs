using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;



namespace JOIN.Infrastructure.Security.Jwt;



/// <summary>
/// Generates signed JWT access tokens for authenticated JOIN users.
/// </summary>
/// <param name="configuration">Application configuration used to read JWT settings.</param>
public class JwtTokenGenerator(IConfiguration configuration) : IJwtTokenGenerator
{

    private readonly IConfiguration _configuration = configuration;


    /// <summary>
    /// Generates a signed access token for the supplied user session. Does NOT generate a refresh
    /// token — <paramref name="refreshTokenString"/> must already be the one the caller persisted
    /// to <c>Security.UserRefreshTokens</c> for <paramref name="refreshTokenId"/>; it is returned
    /// as-is. (Bug fixed 2026-09-12: this used to call <see cref="GenerateRefreshTokenString"/> again
    /// here, handing the client a *different* random string than the one actually persisted —
    /// every refresh attempt failed with "invalid, expired, or revoked" because the string sent back
    /// to log in with never matched any row.)
    /// </summary>
    /// <param name="user">The authenticated application user.</param>
    /// <param name="companyId">The effective company identifier for the session, if one is available.</param>
    /// <param name="roles">All effective role names for the session.</param>
    /// <param name="refreshTokenId">The identifier of the persisted <c>Security.UserRefreshTokens</c> row.
    /// Embedded as the <c>refresh_token_id</c> claim so the access token can later be tied back to
    /// its persisted refresh token record.</param>
    /// <param name="refreshTokenString">The exact string already persisted for <paramref name="refreshTokenId"/>; echoed back, never regenerated.</param>
    /// <returns>A tuple containing the access token, refresh token, and their expiration metadata.</returns>
    public (string Token, string RefreshToken, DateTime Expiration, DateTime RefreshTokenExpiration) GenerateToken(ApplicationUser user, Guid? companyId, IEnumerable<string> roles, Guid refreshTokenId, string refreshTokenString)
    {
        var issuer = _configuration["Jwt:Issuer"] ?? "JOIN.Services.WebApi";
        var audience = _configuration["Jwt:Audience"] ?? "JOIN.Client";
        var key = _configuration["Jwt:Key"] ?? "JOIN_Development_Key_Change_This_In_Production_2026!";

        var expirationMinutes = int.TryParse(_configuration["Jwt:ExpirationMinutes"], out var parsedMinutes)
            ? parsedMinutes
            : 60;

        var refreshTokenExpirationDays = int.TryParse(_configuration["Jwt:RefreshTokenExpirationDays"], out var parsedDays)
            ? parsedDays
            : 30;

        var expiration = DateTime.UtcNow.AddMinutes(expirationMinutes);
        var refreshTokenExpiration = DateTime.UtcNow.AddDays(refreshTokenExpirationDays);
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var effectiveRoles = roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (effectiveRoles.Length == 0)
        {
            effectiveRoles = ["Basic"];
        }

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty)
        };

        foreach (var role in effectiveRoles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        if (companyId.HasValue && companyId.Value != Guid.Empty)
        {
            claims.Add(new Claim("CompanyId", companyId.Value.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(user.FirstName))
        {
            claims.Add(new Claim("FirstName", user.FirstName));
        }

        if (!string.IsNullOrWhiteSpace(user.LastName))
        {
            claims.Add(new Claim("LastName", user.LastName));
        }

        if (refreshTokenId != Guid.Empty)
        {
            claims.Add(new Claim("refresh_token_id", refreshTokenId.ToString()));
        }

        var tokenDescriptor = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiration,
            signingCredentials: credentials);

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.WriteToken(tokenDescriptor);

        return (token, refreshTokenString, expiration, refreshTokenExpiration);
    }

    /// <summary>
    /// Generates a cryptographically secure refresh token string suitable for persistence
    /// in <c>Security.UserRefreshTokens</c>.
    /// </summary>
    /// <returns>A random Base64-encoded refresh token (64 random bytes encoded).</returns>
    public string GenerateRefreshTokenString()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }

    /// <inheritdoc />
    public DateTime GetRefreshTokenExpirationUtc()
    {
        var refreshTokenExpirationDays = int.TryParse(_configuration["Jwt:RefreshTokenExpirationDays"], out var parsedDays)
            ? parsedDays
            : 30;
        return DateTime.UtcNow.AddDays(refreshTokenExpirationDays);
    }
}
