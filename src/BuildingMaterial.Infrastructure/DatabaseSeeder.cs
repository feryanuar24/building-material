using BuildingMaterial.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BuildingMaterial.Infrastructure;

public sealed class DatabaseSeeder(AppDbContext db, IConfiguration configuration)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var adminEmail = "admin@building-material.local";
        var adminPassword = "Admin123!ChangeMe";
        var customerPassword = "Customer123!ChangeMe";

        await AddUserIfMissingAsync(adminEmail, adminPassword, Roles.Admin, "Administrator", cancellationToken);

        var customers = new[]
        {
            ("customer1@building-material.local", "Customer One"),
            ("customer2@building-material.local", "Customer Two"),
            ("customer3@building-material.local", "Customer Three")
        };

        foreach (var (email, fullName) in customers)
            await AddUserIfMissingAsync(email, customerPassword, Roles.Customer, fullName, cancellationToken);

        await SeedCatalogAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedCatalogAsync(CancellationToken cancellationToken)
    {
        var cement = await GetOrAddCategoryAsync(
            "Semen",
            "semen",
            cancellationToken);
        var sand = await GetOrAddCategoryAsync(
            "Pasir",
            "pasir",
            cancellationToken);

        await GetOrAddProductAsync(
            cement,
            "Semen Portland Komposit",
            "semen-portland-komposit",
            "Semen serbaguna untuk pekerjaan konstruksi umum.",
            ("SPK-40KG", 75000m, "bag", 40m),
            cancellationToken);
        await GetOrAddProductAsync(
            sand,
            "Pasir Beton",
            "pasir-beton",
            "Pasir beton berkualitas untuk campuran beton.",
            ("PASIR-BETON-M3", 350000m, "cubic-meter", null),
            cancellationToken);
    }

    private async Task<Category> GetOrAddCategoryAsync(
        string name,
        string slug,
        CancellationToken cancellationToken)
    {
        var category = await db.Categories
            .SingleOrDefaultAsync(x => x.Slug == slug, cancellationToken);
        if (category is not null)
            return category;

        category = new Category { Name = name, Slug = slug };
        db.Categories.Add(category);
        return category;
    }

    private async Task GetOrAddProductAsync(
        Category category,
        string name,
        string slug,
        string description,
        (string Sku, decimal Price, string Unit, decimal? WeightKg) variant,
        CancellationToken cancellationToken)
    {
        var product = await db.Products
            .Include(x => x.Variants)
            .SingleOrDefaultAsync(x => x.Slug == slug, cancellationToken);
        if (product is null)
        {
            product = new Product
            {
                Category = category,
                Name = name,
                Slug = slug,
                Description = description
            };
            db.Products.Add(product);
        }

        if (!product.Variants.Any(x => x.Sku == variant.Sku))
        {
            product.Variants.Add(new ProductVariant
            {
                Sku = variant.Sku,
                Price = variant.Price,
                Unit = variant.Unit,
                WeightKg = variant.WeightKg
            });
        }
    }

    private async Task AddUserIfMissingAsync(
        string email,
        string password,
        string role,
        string name,
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .Include(x => x.Customer)
            .Include(x => x.Admin)
            .SingleOrDefaultAsync(x => x.Email == email, cancellationToken);

        if (user is not null)
            return;

        user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            EmailVerified = true,
            Role = role
        };

        if (role == Roles.Admin)
            user.Admin = new Admin { UserId = user.Id, DisplayName = name };
        else
            user.Customer = new Customer { UserId = user.Id, FullName = name };

        db.Users.Add(user);
    }

    private string GetRequired(string key) =>
        configuration[key] ?? throw new InvalidOperationException($"{key} is not configured.");
}
