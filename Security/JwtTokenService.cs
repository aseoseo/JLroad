using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using JIroad.Data.Entities;
using Microsoft.IdentityModel.Tokens;

namespace JIroad.Security;

public sealed class JwtTokenService(IConfiguration configuration)
{
    public string Create(AppUser user)
    {
        var key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("Missing Jwt:Key");
        var issuer = configuration["Jwt:Issuer"] ?? "JIroad";
        var audience = configuration["Jwt:Audience"] ?? "JIroadClient";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        // ОБНОВЛЕНО: Строго фиксируем UTC-вид для даты окончания токена, чтобы избежать ошибок валидации в Docker
        var expiresUtc = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(7), DateTimeKind.Utc);

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: expiresUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}