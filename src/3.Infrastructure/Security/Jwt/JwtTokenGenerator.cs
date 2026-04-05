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
    /// Generates a signed access token and a secure refresh token for the supplied user session.
    /// </summary>
    /// <param name="user">The authenticated application user.</param>
    /// <param name="companyId">The effective company identifier for the session, if one is available.</param>
    /// <param name="roles">All effective role names for the session.</param>
    /// <returns>A tuple containing the access token, refresh token, and their expiration metadata.</returns>
    public (string Token, string RefreshToken, DateTime Expiration, DateTime RefreshTokenExpiration) GenerateToken(ApplicationUser user, Guid? companyId, IEnumerable<string> roles)
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

        var tokenDescriptor = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiration,
            signingCredentials: credentials);

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.WriteToken(tokenDescriptor);
        var refreshToken = GenerateSecureRefreshToken();

        return (token, refreshToken, expiration, refreshTokenExpiration);
    }

    /// <summary>
    /// Generates a cryptographically secure refresh token string.
    /// </summary>
    /// <returns>A random Base64Url-encoded refresh token.</returns>
    private static string GenerateSecureRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }
}
