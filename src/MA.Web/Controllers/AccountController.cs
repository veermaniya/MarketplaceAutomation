using System.Security.Claims;
using MA.Core.Entities;
using MA.Data;
using MA.Web.Models;
using MA.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MA.Web.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger<AccountController> _log;

    public AccountController(AppDbContext db, IPasswordHasher hasher,
        IJwtTokenService jwt, ILogger<AccountController> log)
    {
        _db = db; _hasher = hasher; _jwt = jwt; _log = log;
    }

    [HttpGet] public IActionResult Login(string? returnUrl = null)
        => View(new LoginViewModel { ReturnUrl = returnUrl });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == vm.Email && u.IsActive);

        if (user is null || !_hasher.Verify(vm.Password, user.PasswordHash, user.PasswordSalt))
        {
            ModelState.AddModelError("", "Invalid credentials");
            return View(vm);
        }

        user.LastLoginOn = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var roles = user.UserRoles.Select(ur => ur.Role!.RoleName).ToList();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Email, user.Email)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        _log.LogInformation("User {Email} logged in", user.Email);

        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl))
            return Redirect(vm.ReturnUrl);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet] public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        if (await _db.Users.AnyAsync(u => u.Email == vm.Email))
        {
            ModelState.AddModelError(nameof(vm.Email), "Email already registered");
            return View(vm);
        }

        var (hash, salt) = _hasher.Hash(vm.Password);
        var user = new User
        {
            UserName     = vm.UserName,
            Email        = vm.Email,
            PasswordHash = hash,
            PasswordSalt = salt
        };

        // First user becomes Admin, subsequent get Manager
        bool anyUser = await _db.Users.AnyAsync();
        var roleName = anyUser ? "Manager" : "Admin";
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName == roleName);
        if (role is not null) user.UserRoles.Add(new UserRole { Role = role });

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    /// <summary>POST /Account/Token  - issues a JWT for API clients.</summary>
    [HttpPost("/Account/Token")]
    public async Task<IActionResult> Token([FromBody] LoginViewModel vm)
    {
        var user = await _db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Email == vm.Email && u.IsActive);

        if (user is null || !_hasher.Verify(vm.Password, user.PasswordHash, user.PasswordSalt))
            return Unauthorized();

        var roles = user.UserRoles.Select(ur => ur.Role!.RoleName);
        return Ok(new { token = _jwt.CreateToken(user, roles) });
    }

    public IActionResult AccessDenied() => View();
}
