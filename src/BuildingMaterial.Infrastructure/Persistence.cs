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
