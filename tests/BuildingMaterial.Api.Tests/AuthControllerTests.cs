using BuildingMaterial.Api;
using BuildingMaterial.Application;
using BuildingMaterial.Domain;
using BuildingMaterial.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BuildingMaterial.Api.Tests;

public sealed class AuthControllerTests
{
    [Fact]
    public async Task Register_creates_normalized_customer_and_sends_verification_token()
    {
        await using var db = CreateDb();
        var email = new FakeEmailSender();
        var controller = CreateController(db, email);

        var result = await controller.Register(
            new RegisterRequest
            {
                Email = "  CUSTOMER@Example.COM ",
                Password = "correct horse battery staple",
                FullName = "  Jane Doe  "
            },
            CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, response.StatusCode);
        var user = await db.Users.SingleAsync();
        Assert.Equal("customer@example.com", user.Email);
        Assert.Equal("Jane Doe", user.Customer!.FullName);
        Assert.False(user.EmailVerified);
        Assert.Single(db.EmailTokens);
        Assert.Equal("customer@example.com", email.LastRecipient);
        Assert.Contains("Use this verification token:", email.LastBody);
    }

    [Fact]
    public async Task Register_returns_conflict_for_existing_email_case_insensitively()
    {
        await using var db = CreateDb();
        db.Users.Add(new User { Email = "customer@example.com", PasswordHash = "hash" });
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FakeEmailSender());

        var result = await controller.Register(
            new RegisterRequest
            {
                Email = "CUSTOMER@example.com",
                Password = "correct horse battery staple",
                FullName = "Jane Doe"
            },
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Single(db.Users);
    }

    [Fact]
    public async Task Login_returns_unauthorized_for_invalid_password()
    {
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Email = "customer@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct password"),
            EmailVerified = true
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FakeEmailSender());

        var result = await controller.Login(
            new LoginRequest { Email = "customer@example.com", Password = "wrong password" },
            CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Login_returns_forbidden_until_email_is_verified()
    {
        await using var db = CreateDb();
        db.Users.Add(new User
        {
            Email = "customer@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct password")
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FakeEmailSender());

        var result = await controller.Login(
            new LoginRequest { Email = "customer@example.com", Password = "correct password" },
            CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task VerifyEmail_marks_matching_token_as_used()
    {
        await using var db = CreateDb();
        var user = new User { Email = "customer@example.com" };
        const string rawToken = "verification-token";
        var token = new EmailToken
        {
            User = user,
            UserId = user.Id,
            Purpose = "verify-email",
            TokenHash = TokenHasher.Hash(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        db.Add(token);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FakeEmailSender());

        var result = await controller.VerifyEmail(
            new EmailTokenRequest { Email = user.Email, Token = rawToken },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.True((await db.Users.SingleAsync()).EmailVerified);
        Assert.True((await db.EmailTokens.SingleAsync()).Used);
    }

    [Fact]
    public async Task ResetEmail_changes_password_and_consumes_reset_token()
    {
        await using var db = CreateDb();
        var user = new User
        {
            Email = "customer@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("old password")
        };
        const string rawToken = "reset-token";
        db.Add(new EmailToken
        {
            User = user,
            UserId = user.Id,
            Purpose = "reset-email",
            TokenHash = TokenHasher.Hash(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, new FakeEmailSender());

        var result = await controller.ResetEmail(
            new ResetPasswordRequest
            {
                Email = user.Email,
                Token = rawToken,
                NewPassword = "new correct password"
            },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var updatedUser = await db.Users.SingleAsync();
        Assert.True(BCrypt.Net.BCrypt.Verify("new correct password", updatedUser.PasswordHash));
        Assert.False(BCrypt.Net.BCrypt.Verify("old password", updatedUser.PasswordHash));
        Assert.True((await db.EmailTokens.SingleAsync()).Used);
    }

    private static AuthController CreateController(AppDbContext db, FakeEmailSender email)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["Jwt:Key"] = "test-key-that-is-at-least-32-characters-long",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience"
            })
            .Build();
        var recaptcha = new RecaptchaValidator(configuration, new FakeHttpClientFactory());
        var controller = new AuthController(db, new JwtTokenService(configuration), email, recaptcha);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public string? LastRecipient { get; private set; }
        public string? LastBody { get; private set; }

        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
        {
            LastRecipient = recipient;
            LastBody = body;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
