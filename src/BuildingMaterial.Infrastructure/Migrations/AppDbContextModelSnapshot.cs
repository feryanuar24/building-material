using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace BuildingMaterial.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
partial class AppDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.10");

        modelBuilder.Entity("BuildingMaterial.Domain.Admin", b =>
        {
            b.Property<Guid>("Id").HasColumnName("id").HasColumnType("uuid");
            b.Property<string>("DisplayName").HasColumnName("display_name");
            b.Property<Guid>("UserId").HasColumnName("user_id");
            b.HasKey("Id");
            b.HasIndex("UserId").IsUnique();
            b.ToTable("admins");
        });

        modelBuilder.Entity("BuildingMaterial.Domain.Customer", b =>
        {
            b.Property<Guid>("Id").HasColumnName("id").HasColumnType("uuid");
            b.Property<string>("FullName").HasColumnName("full_name");
            b.Property<Guid>("UserId").HasColumnName("user_id");
            b.HasKey("Id");
            b.HasIndex("UserId").IsUnique();
            b.ToTable("customers");
        });

        modelBuilder.Entity("BuildingMaterial.Domain.EmailToken", b =>
        {
            b.Property<Guid>("Id").HasColumnName("id").HasColumnType("uuid");
            b.Property<DateTimeOffset>("ExpiresAt").HasColumnName("expires_at");
            b.Property<string>("Purpose").HasColumnName("purpose");
            b.Property<string>("TokenHash").HasColumnName("token_hash");
            b.Property<Guid>("UserId").HasColumnName("user_id");
            b.Property<bool>("Used").HasColumnName("used");
            b.HasKey("Id");
            b.HasIndex("UserId", "Purpose", "TokenHash");
            b.ToTable("email_tokens");
        });

        modelBuilder.Entity("BuildingMaterial.Domain.User", b =>
        {
            b.Property<Guid>("Id").HasColumnName("id").HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnName("created_at");
            b.Property<string>("Email").HasColumnName("email").HasMaxLength(320).HasColumnType("character varying(320)");
            b.Property<bool>("EmailVerified").HasColumnName("email_verified");
            b.Property<string>("PasswordHash").HasColumnName("password_hash");
            b.Property<string>("Role").HasColumnName("role").HasMaxLength(32).HasColumnType("character varying(32)");
            b.HasKey("Id");
            b.HasIndex("Email").IsUnique();
            b.ToTable("users");
        });

        modelBuilder.Entity("BuildingMaterial.Domain.Admin", b =>
        {
            b.HasOne("BuildingMaterial.Domain.User", "User")
                .WithOne("Admin")
                .HasForeignKey("BuildingMaterial.Domain.Admin", "UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("User");
        });

        modelBuilder.Entity("BuildingMaterial.Domain.Customer", b =>
        {
            b.HasOne("BuildingMaterial.Domain.User", "User")
                .WithOne("Customer")
                .HasForeignKey("BuildingMaterial.Domain.Customer", "UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("User");
        });

        modelBuilder.Entity("BuildingMaterial.Domain.EmailToken", b =>
        {
            b.HasOne("BuildingMaterial.Domain.User", "User")
                .WithMany()
                .HasForeignKey("UserId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
            b.Navigation("User");
        });
    }
}
