using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MA.Core.Entities;
using Microsoft.IdentityModel.Tokens;

namespace MA.Web.Services;

public interface IJwtTokenService
{
    string CreateToken(User user, IEnumerable<string> roles);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _cfg;
    public JwtTokenService(IConfiguration cfg) { _cfg = cfg; }

    public string CreateToken(User user, IEnumerable<string> roles)
    {
        var jwt = _cfg.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Name, user.UserName)
        };
        foreach (var r in roles)
            claims.Add(new Claim(ClaimTypes.Role, r));

        var token = new JwtSecurityToken(
            issuer:   jwt["Issuer"],
            audience: jwt["Audience"],
            claims:   claims,
            expires:  DateTime.UtcNow.AddHours(int.Parse(jwt["ExpiryHours"] ?? "8")),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
