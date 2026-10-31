using System.Text;
using BuildingMaterial.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using BuildingMaterial.Infrastructure;

// Inisialisasi Aplikasi
var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// Registrasi Logger ke File
builder.Logging.AddProvider(new FileLoggerProvider(
    Path.Combine(builder.Environment.ContentRootPath, "logs", "app.log")));

// Registrasi EFC menggunakan PostgreSQL serta mengatur migrasi
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(configuration.GetConnectionString("Postgres"),
        npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

// Registrasi Error Handling
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();

// Registrasi Service Custom (JWT Token Service, Email Sender, Database Seeder)
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<DatabaseSeeder>();

// Registrasi HTTP Client
builder.Services.AddHttpClient();

// Registrasi HTTP Client untuk Elasticsearch
builder.Services.AddHttpClient("elasticsearch", client =>
{
    client.BaseAddress = new Uri(configuration["Elasticsearch:Url"] ?? "http://elasticsearch:9200/");
    client.Timeout = TimeSpan.FromSeconds(configuration.GetValue("Elasticsearch:TimeoutSeconds", 3));
});

// Registrasi Redis
builder.Services.AddStackExchangeRedisCache(options => options.Configuration = configuration.GetConnectionString("Redis") ?? "redis:6379");

// Registrasi Service Custom (CategoryCache & ElasticsearchCatalog)
builder.Services.AddScoped<CategoryCache>();
builder.Services.AddSingleton<ElasticsearchCatalog>();

// Registrasi Background Service untuk Sinkronisasi Catalog ke Elasticsearch
builder.Services.AddHostedService<CatalogSyncWorker>();

// Registrasi HTTP Client untuk Slack Error Notifier
builder.Services.AddHttpClient<SlackErrorNotifier>();

// Registrasi reCAPTCHA Validator
builder.Services.AddSingleton<RecaptchaValidator>();

// Registrasi Authentication dengan JWT Bearer dan Authorization
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = configuration["Jwt:Issuer"],
        ValidAudience = configuration["Jwt:Audience"],
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

// Registrasi Rate Limiter
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

// Registrasi Endpoint, Swagger, & Health Checks
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks().AddNpgSql(configuration.GetConnectionString("Postgres")!);

// Build Aplikasi
var app = builder.Build();

// Menerapkan Migrasi Database dan Seed Data
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
}

// Jalankan Middleware dan Endpoint
app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllers();
app.Run();
