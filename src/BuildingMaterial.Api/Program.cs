using System.Text;
using BuildingMaterial.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using BuildingMaterial.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("Postgres"),
        npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<DatabaseSeeder>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("elasticsearch", client =>
{
    client.BaseAddress = new Uri(configuration["Elasticsearch:Url"] ?? "http://elasticsearch:9200/");
    client.Timeout = TimeSpan.FromSeconds(configuration.GetValue("Elasticsearch:TimeoutSeconds", 3));
});
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = configuration.GetConnectionString("Redis") ?? "redis:6379");
builder.Services.AddScoped<CategoryCache>();
builder.Services.AddSingleton<ElasticsearchCatalog>();
builder.Services.AddHostedService<CatalogSyncWorker>();
builder.Services.AddHttpClient<SlackErrorNotifier>();
builder.Services.AddSingleton<RecaptchaValidator>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ValidIssuer = configuration["Jwt:Issuer"], ValidAudience = configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.")))
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (string.IsNullOrWhiteSpace(context.Token) && context.Request.Cookies.TryGetValue("access_token", out var cookie))
                context.Token = cookie;
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = configuration.GetValue("RateLimiting:AuthPermitLimit", 10);
        limiter.Window = TimeSpan.FromMinutes(configuration.GetValue("RateLimiting:AuthWindowMinutes", 1));
        limiter.QueueLimit = 0;
    });
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks().AddNpgSql(configuration.GetConnectionString("Postgres")!);

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllers();
app.Run();

public sealed class RecaptchaValidator(IConfiguration configuration, IHttpClientFactory httpClientFactory)
{
    public async Task EnsureValidAsync(string? token, CancellationToken cancellationToken)
    {
        if (!string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Production", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase))
            return;
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("reCAPTCHA token is required.");
        var secret = configuration["Recaptcha:SecretKey"] ?? throw new InvalidOperationException("Recaptcha:SecretKey is not configured.");
        using var response = await httpClientFactory.CreateClient().PostAsync(
            $"https://www.google.com/recaptcha/api/siteverify?secret={Uri.EscapeDataString(secret)}&response={Uri.EscapeDataString(token)}",
            null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<RecaptchaResponse>(cancellationToken);
        if (result is not { Success: true }) throw new InvalidOperationException("reCAPTCHA validation failed.");
    }

    private sealed record RecaptchaResponse(bool Success);
}
