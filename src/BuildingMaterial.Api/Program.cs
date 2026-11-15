using System.Text;
using BuildingMaterial.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using BuildingMaterial.Infrastructure;
using Serilog;

// Bootstrap logger
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    // Inisialisasi Aplikasi
    var builder = WebApplication.CreateBuilder(args);
    var configuration = builder.Configuration;

    // Registrasi Serilog
    builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "BuildingMaterial.Api")
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName));

    // Registrasi EFC
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(configuration.GetConnectionString("Postgres"),
            npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

    // Registrasi Error Handling
    builder.Services.AddExceptionHandler<ApiExceptionHandler>();
    builder.Services.AddProblemDetails();

    // Registrasi Service Custom
    builder.Services.AddScoped<ITokenService, JwtTokenService>();
    builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
    builder.Services.AddScoped<DatabaseSeeder>();

    // Registrasi HTTP Client
    builder.Services.AddHttpClient();

    // Registrasi Elasticsearch
    builder.Services.AddHttpClient("elasticsearch", client =>
    {
        client.BaseAddress = new Uri(configuration["Elasticsearch:Url"] ?? "http://elasticsearch:9200/");
        client.Timeout = TimeSpan.FromSeconds(configuration.GetValue("Elasticsearch:TimeoutSeconds", 3));
    });

    // Registrasi Redis
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = configuration.GetConnectionString("Redis") ?? "redis:6379");

    // Registrasi Service Custom
    builder.Services.AddScoped<CategoryCache>();
    builder.Services.AddSingleton<ElasticsearchCatalog>();

    // Registrasi Background Service Sinkronisasi Catalog ke Elasticsearch
    builder.Services.AddHostedService<CatalogSyncWorker>();

    // Registrasi Slack Error Notifier
    builder.Services.AddHttpClient<SlackErrorNotifier>();

    // Registrasi Service Custom
    builder.Services.AddSingleton<RecaptchaValidator>();

    // Registrasi Auth dengan JWT Bearer
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
            // Ambil token dari cookie 'access_token' jika tidak ada di header Authorization
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

    // Registrasi CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("DefaultCorsPolicy", policy =>
        {
            var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
            if (allowedOrigins.Length > 0)
            {
                // Batasi origin sesuai konfigurasi jika tersedia
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            }
            else
            {
                // Izinkan semua origin jika belum dikonfigurasi
                policy.SetIsOriginAllowed(_ => true)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            }
        });
    });

    // Registrasi Controllers
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    // Registrasi Swagger/OpenAPI
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Building Material API",
            Version = "v1",
            Description = "API Katalog Bahan Bangunan, Autentikasi, dan Admin Management"
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Description = "Masukkan JWT token Anda. Contoh format: eyJhbGciOi...",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT"
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    // Registrasi Health Checks
    builder.Services.AddHealthChecks().AddNpgSql(configuration.GetConnectionString("Postgres")!);

    var app = builder.Build();

    // Jalankan DB Migration dan Seed data awal saat startup
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    }

    // Jalankan Error Handling
    app.UseExceptionHandler();

    // Jalankan Logging
    app.UseSerilogRequestLogging();

    // Jalankan Swagger UI
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Building Material API v1");
            options.DisplayRequestDuration();
        });
    }

    // Jalankan HTTPS Redirection
    app.UseHttpsRedirection();

    // Jalankan CORS
    app.UseCors("DefaultCorsPolicy");

    // Jalankan Rate Limiter
    app.UseRateLimiter();

    // Jalankan Auth
    app.UseAuthentication();
    app.UseAuthorization();

    // Jalankan Rute Health
    app.MapGet("/health", async (HealthCheckService healthService) =>
    {
        var report = await healthService.CheckHealthAsync();
        return report.Status == HealthStatus.Healthy
            ? Results.Text("Healthy")
            : Results.Text(report.Status.ToString(), statusCode: StatusCodes.Status503ServiceUnavailable);
    })
    .WithTags("Health")
    .Produces(StatusCodes.Status200OK, contentType: "text/plain")
    .Produces(StatusCodes.Status503ServiceUnavailable, contentType: "text/plain");

    // Jalankan Rute Controllers
    app.MapControllers();

    // Jalankan Aplikasi
    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // Abaikan HostAbortedException (biasanya dari EF Core CLI tools) dan log error fatal lainnya
    Log.Fatal(ex, "Application terminated unexpectedly.");
    return 1;
}
finally
{
    // Pastikan semua log tertulis sebelum aplikasi berhenti
    await Log.CloseAndFlushAsync();
}

return 0;
