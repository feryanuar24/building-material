using BuildingMaterial.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BuildingMaterial.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<EmailToken> EmailTokens => Set<EmailToken>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<CatalogOutboxMessage> CatalogOutboxMessages => Set<CatalogOutboxMessage>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess) { EnqueueCatalogChanges(); return base.SaveChanges(acceptAllChangesOnSuccess); }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) { EnqueueCatalogChanges(); return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken); }

    private void EnqueueCatalogChanges()
    {
        var tracked = ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(x => x.Entity is Category or Product or ProductVariant).ToArray();
        foreach (var entry in tracked)
        {
            var (type, id) = entry.Entity switch
            {
                Category x => ("category", x.Id), Product x => ("product", x.Id), ProductVariant x => ("variant", x.Id),
                _ => throw new InvalidOperationException("Unsupported catalog entity.")
            };
            CatalogOutboxMessages.Add(new CatalogOutboxMessage { EntityType = type, EntityId = id, Operation = entry.State == EntityState.Deleted ? "delete" : "upsert" });
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            b.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            b.Property(x => x.EmailVerified).HasColumnName("email_verified");
            b.Property(x => x.Role).HasColumnName("role").HasMaxLength(32).IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at");
            b.HasIndex(x => x.Email).IsUnique();
            b.HasOne(x => x.Customer).WithOne(x => x.User).HasForeignKey<Customer>(x => x.UserId);
            b.HasOne(x => x.Admin).WithOne(x => x.User).HasForeignKey<Admin>(x => x.UserId);
        });
        modelBuilder.Entity<Customer>(b =>
        {
            b.ToTable("customers");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.FullName).HasColumnName("full_name");
        });
        modelBuilder.Entity<Admin>(b =>
        {
            b.ToTable("admins");
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.DisplayName).HasColumnName("display_name");
        });
        modelBuilder.Entity<EmailToken>(b =>
        {
            b.ToTable("email_tokens");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id");
            b.Property(x => x.UserId).HasColumnName("user_id");
            b.Property(x => x.TokenHash).HasColumnName("token_hash");
            b.Property(x => x.Purpose).HasColumnName("purpose");
            b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            b.Property(x => x.Used).HasColumnName("used");
            b.HasIndex(x => new { x.UserId, x.Purpose, x.TokenHash });
            b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });
        modelBuilder.Entity<Category>(b =>
        {
            b.ToTable("categories"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(220).IsRequired(); b.Property(x => x.ParentId).HasColumnName("parent_id");
            b.HasIndex(x => x.Slug).IsUnique(); b.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("products"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CategoryId).HasColumnName("category_id");
            b.Property(x => x.Name).HasColumnName("name").HasMaxLength(300).IsRequired(); b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(320).IsRequired();
            b.Property(x => x.Description).HasColumnName("description").IsRequired(); b.Property(x => x.IsActive).HasColumnName("is_active");
            b.Property(x => x.UpdatedAt).HasColumnName("updated_at"); b.HasIndex(x => x.Slug).IsUnique();
            b.HasOne(x => x.Category).WithMany(x => x.Products).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ProductVariant>(b =>
        {
            b.ToTable("product_variants"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.ProductId).HasColumnName("product_id");
            b.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(100).IsRequired(); b.Property(x => x.Price).HasColumnName("price").HasPrecision(18, 2);
            foreach (var name in new[] { "LengthMm", "WidthMm", "HeightMm", "DiameterMm", "ThicknessMm", "WeightKg" })
                b.Property<decimal?>(name).HasColumnName(char.ToLowerInvariant(name[0]) + name[1..]);
            b.Property(x => x.Unit).HasColumnName("unit").HasMaxLength(30).IsRequired(); b.Property(x => x.IsActive).HasColumnName("is_active");
            b.HasIndex(x => x.Sku).IsUnique(); b.HasOne(x => x.Product).WithMany(x => x.Variants).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CatalogOutboxMessage>(b =>
        {
            b.ToTable("catalog_outbox"); b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(50).IsRequired();
            b.Property(x => x.EntityId).HasColumnName("entity_id"); b.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(20).IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            b.Property(x => x.Attempts).HasColumnName("attempts"); b.Property(x => x.LastError).HasColumnName("last_error");
            b.HasIndex(x => new { x.ProcessedAt, x.CreatedAt });
        });
    }
}

public interface IEmailSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken);
}

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        using var message = new MimeKit.MimeMessage();
        message.From.Add(MimeKit.MailboxAddress.Parse(configuration["Smtp:From"] ?? "no-reply@example.com"));
        message.To.Add(MimeKit.MailboxAddress.Parse(recipient));
        message.Subject = subject;
        message.Body = new MimeKit.TextPart("plain") { Text = body };
        using var client = new MailKit.Net.Smtp.SmtpClient();
        await client.ConnectAsync(configuration["Smtp:Host"] ?? "mailer", configuration.GetValue("Smtp:Port", 1025), false, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
