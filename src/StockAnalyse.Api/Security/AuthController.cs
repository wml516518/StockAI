using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace StockAnalyse.Api.Security;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    public const string LoginRateLimitPolicy = "login";

    private readonly AppAuthSettings _settings;

    public AuthController(AppAuthSettings settings)
    {
        _settings = settings;
    }

    [AllowAnonymous]
    [HttpGet("session")]
    public IActionResult Session()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            authenticated = User.Identity?.IsAuthenticated == true,
            username = User.Identity?.IsAuthenticated == true
                ? User.Identity.Name
                : null
        });
    }

    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username)
            || string.IsNullOrEmpty(request.Password)
            || !_settings.IsValid(request.Username.Trim(), request.Password))
        {
            Response.Headers.CacheControl = "no-store";
            return Unauthorized(new { message = "用户名或密码不正确" });
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, _settings.Username),
            new Claim(ClaimTypes.NameIdentifier, _settings.Username)
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            AllowRefresh = true
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            authenticated = true,
            username = _settings.Username
        });
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }
}

public sealed record LoginRequest(string Username, string Password);
