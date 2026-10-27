using System.Security.Claims;
using BuildingMaterial.Application;
using BuildingMaterial.Domain;
using BuildingMaterial.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BuildingMaterial.Api;

[ApiController, Route("api/auth"), EnableRateLimiting("auth")]
public sealed class AuthController(AppDbContext db, ITokenService tokens, IEmailSender email, RecaptchaValidator recaptcha) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        await recaptcha.EnsureValidAsync(request.RecaptchaToken, ct);
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Email == normalizedEmail, ct)) return Conflict(new { message = "Email is already registered." });
        var user = new User { Email = normalizedEmail, PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password), Role = Roles.Customer };
        user.Customer = new Customer { UserId = user.Id, FullName = request.FullName.Trim() };
        db.Users.Add(user);
        var rawToken = CreateEmailToken(user, "verify-email");
        await db.SaveChangesAsync(ct);
        await email.SendAsync(user.Email, "Verify your email", $"Use this verification token: {rawToken}", ct);
        return StatusCode(StatusCodes.Status201Created, new { message = "Registration successful. Check your email to verify your account." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        await recaptcha.EnsureValidAsync(request.RecaptchaToken, ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == request.Email.Trim().ToLowerInvariant(), ct);
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid credentials." });
        if (!user.EmailVerified) return StatusCode(StatusCodes.Status403Forbidden, new { message = "Email must be verified." });
        return Ok(IssueTokens(user));
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(EmailTokenRequest request, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == request.Email.Trim().ToLowerInvariant(), ct);
        var token = await ValidToken(user, request.Token, request.RecaptchaToken, "verify-email", ct);
        if (token is null || user is null) return BadRequest(new { message = "Invalid or expired verification token." });
        user.EmailVerified = true; token.Used = true;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Email verified." });
    }

    [HttpPost("forgot-email")]
    public async Task<IActionResult> ForgotEmail(ForgotEmailRequest request, CancellationToken ct)
    {
        await recaptcha.EnsureValidAsync(request.RecaptchaToken, ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == request.Email.Trim().ToLowerInvariant(), ct);
        if (user is not null)
        {
            var rawToken = CreateEmailToken(user, "reset-email");
            await db.SaveChangesAsync(ct);
            await email.SendAsync(user.Email, "Reset your password", $"Use this reset token: {rawToken}", ct);
        }
        return Ok(new { message = "If the email exists, reset instructions have been sent." });
    }

    [HttpPost("reset-email")]
    public async Task<IActionResult> ResetEmail(ResetPasswordRequest request, CancellationToken ct)
    {
        await recaptcha.EnsureValidAsync(request.RecaptchaToken, ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == request.Email.Trim().ToLowerInvariant(), ct);
        var token = user is null ? null : await db.EmailTokens.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Purpose == "reset-email" && !x.Used && x.ExpiresAt > DateTimeOffset.UtcNow && x.TokenHash == TokenHasher.Hash(request.Token), ct);
        if (token is null || user is null) return BadRequest(new { message = "Invalid or expired reset token." });
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword); token.Used = true;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Password reset successfully." });
    }

    [Authorize(Roles = Roles.Admin), HttpGet("/api/admin/me")]
    public IActionResult AdminOnly() => Ok(new { message = "Admin access granted.", subject = User.FindFirstValue(ClaimTypes.Email) });

    private AuthResponse IssueTokens(User user)
    {
        var (token, expires) = tokens.Create(user);
        Response.Cookies.Append("access_token", token, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Expires = expires });
        return new AuthResponse(token, expires, user.Role);
    }

    private string CreateEmailToken(User user, string purpose)
    {
        var raw = TokenHasher.CreateRaw();
        db.EmailTokens.Add(new EmailToken { UserId = user.Id, Purpose = purpose, TokenHash = TokenHasher.Hash(raw), ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
        return raw;
    }

    private async Task<EmailToken?> ValidToken(User? user, string rawToken, string? recaptchaToken, string purpose, CancellationToken ct)
    {
        await recaptcha.EnsureValidAsync(recaptchaToken, ct);
        return user is null ? null : await db.EmailTokens.SingleOrDefaultAsync(x => x.UserId == user.Id && x.Purpose == purpose && !x.Used && x.ExpiresAt > DateTimeOffset.UtcNow && x.TokenHash == TokenHasher.Hash(rawToken), ct);
    }
}
