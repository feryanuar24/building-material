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

        await db.SaveChangesAsync(cancellationToken);
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
