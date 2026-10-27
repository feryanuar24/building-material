using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace BuildingMaterial.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261027000000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("users", table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false),
            email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
            password_hash = table.Column<string>(type: "text", nullable: false),
            email_verified = table.Column<bool>(type: "boolean", nullable: false),
            role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
            created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_users", x => x.id));

        migrationBuilder.CreateTable("admins", table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false),
            user_id = table.Column<Guid>(type: "uuid", nullable: false),
            display_name = table.Column<string>(type: "text", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_admins", x => x.id);
            table.ForeignKey("FK_admins_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("customers", table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false),
            user_id = table.Column<Guid>(type: "uuid", nullable: false),
            full_name = table.Column<string>(type: "text", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_customers", x => x.id);
            table.ForeignKey("FK_customers_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("email_tokens", table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false),
            user_id = table.Column<Guid>(type: "uuid", nullable: false),
            token_hash = table.Column<string>(type: "text", nullable: false),
            purpose = table.Column<string>(type: "text", nullable: false),
            expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            used = table.Column<bool>(type: "boolean", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_email_tokens", x => x.id);
            table.ForeignKey("FK_email_tokens_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateIndex("IX_users_email", "users", "email", unique: true);
        migrationBuilder.CreateIndex("IX_admins_user_id", "admins", "user_id", unique: true);
        migrationBuilder.CreateIndex("IX_customers_user_id", "customers", "user_id", unique: true);
        migrationBuilder.CreateIndex("IX_email_tokens_lookup", "email_tokens", new[] { "user_id", "purpose", "token_hash" });
        migrationBuilder.CreateIndex("IX_email_tokens_user_id", "email_tokens", "user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("admins");
        migrationBuilder.DropTable("customers");
        migrationBuilder.DropTable("email_tokens");
        migrationBuilder.DropTable("users");
    }
}
