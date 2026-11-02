using BuildingMaterial.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace BuildingMaterial.Api.Tests;

public sealed class SwaggerTests
{
    [Fact]
    public void Swagger_document_contains_all_routes_and_bearer_security()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var env = new TestWebHostEnvironment();
        services.AddSingleton<IHostEnvironment>(env);
        services.AddSingleton<IWebHostEnvironment>(env);
        services.AddControllers().AddApplicationPart(typeof(CatalogController).Assembly);
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Building Material API",
                Version = "v1"
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header
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

        using var provider = services.BuildServiceProvider();
        var swaggerProvider = provider.GetRequiredService<ISwaggerProvider>();
        var doc = swaggerProvider.GetSwagger("v1");

        Assert.NotNull(doc);
        Assert.True(doc.Components.SecuritySchemes.ContainsKey("Bearer"));
        Assert.Equal(SecuritySchemeType.Http, doc.Components.SecuritySchemes["Bearer"].Type);

        // Verify key routes
        Assert.True(doc.Paths.ContainsKey("/api/catalog"));
        Assert.True(doc.Paths.ContainsKey("/api/catalog/categories"));
        Assert.True(doc.Paths.ContainsKey("/api/auth/login"));
        Assert.True(doc.Paths.ContainsKey("/api/auth/register"));
        Assert.True(doc.Paths.ContainsKey("/api/admin/catalog/categories"));
        Assert.True(doc.Paths.ContainsKey("/api/admin/catalog/products"));
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "BuildingMaterial.Api";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Development";
    }
}
