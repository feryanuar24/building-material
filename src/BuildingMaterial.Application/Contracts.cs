using System.ComponentModel.DataAnnotations;

namespace BuildingMaterial.Application;

public sealed class RegisterRequest
{
    [Required, EmailAddress] public string Email { get; init; } = string.Empty;
    [Required, MinLength(12)] public string Password { get; init; } = string.Empty;
    [Required, MinLength(2)] public string FullName { get; init; } = string.Empty;
    public string? RecaptchaToken { get; init; }
}

public sealed class LoginRequest
{
    [Required, EmailAddress] public string Email { get; init; } = string.Empty;
    [Required] public string Password { get; init; } = string.Empty;
    public string? RecaptchaToken { get; init; }
}

public sealed class EmailTokenRequest
{
    [Required, EmailAddress] public string Email { get; init; } = string.Empty;
    [Required] public string Token { get; init; } = string.Empty;
    public string? RecaptchaToken { get; init; }
}

public sealed class ForgotEmailRequest
{
    [Required, EmailAddress] public string Email { get; init; } = string.Empty;
    public string? RecaptchaToken { get; init; }
}

public sealed class ResetPasswordRequest
{
    [Required, EmailAddress] public string Email { get; init; } = string.Empty;
    [Required] public string Token { get; init; } = string.Empty;
    [Required, MinLength(12)] public string NewPassword { get; init; } = string.Empty;
    public string? RecaptchaToken { get; init; }
}

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, string Role);
