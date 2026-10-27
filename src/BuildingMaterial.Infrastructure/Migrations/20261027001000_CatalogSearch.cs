using BuildingMaterial.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildingMaterial.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261027001000_CatalogSearch")]
public partial class CatalogSearch : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("categories", t => new { id = t.Column<Guid>("uuid", nullable: false), name = t.Column<string>("character varying(200)", maxLength: 200, nullable: false), slug = t.Column<string>("character varying(220)", maxLength: 220, nullable: false), parent_id = t.Column<Guid>("uuid", nullable: true) }, c => { c.PrimaryKey("PK_categories", x => x.id); c.ForeignKey("FK_categories_categories_parent_id", x => x.parent_id, "categories", "id", onDelete: ReferentialAction.Restrict); });
        m.CreateTable("products", t => new { id = t.Column<Guid>("uuid", nullable: false), category_id = t.Column<Guid>("uuid", nullable: false), name = t.Column<string>("character varying(300)", maxLength: 300, nullable: false), slug = t.Column<string>("character varying(320)", maxLength: 320, nullable: false), description = t.Column<string>("text", nullable: false), is_active = t.Column<bool>("boolean", nullable: false), updated_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false) }, c => { c.PrimaryKey("PK_products", x => x.id); c.ForeignKey("FK_products_categories_category_id", x => x.category_id, "categories", "id", onDelete: ReferentialAction.Restrict); });
        m.CreateTable("product_variants", t => new { id = t.Column<Guid>("uuid", nullable: false), product_id = t.Column<Guid>("uuid", nullable: false), sku = t.Column<string>("character varying(100)", maxLength: 100, nullable: false), price = t.Column<decimal>("numeric(18,2)", nullable: false), lengthMm = t.Column<decimal>("numeric", nullable: true), widthMm = t.Column<decimal>("numeric", nullable: true), heightMm = t.Column<decimal>("numeric", nullable: true), diameterMm = t.Column<decimal>("numeric", nullable: true), thicknessMm = t.Column<decimal>("numeric", nullable: true), weightKg = t.Column<decimal>("numeric", nullable: true), unit = t.Column<string>("character varying(30)", maxLength: 30, nullable: false), is_active = t.Column<bool>("boolean", nullable: false) }, c => { c.PrimaryKey("PK_product_variants", x => x.id); c.ForeignKey("FK_product_variants_products_product_id", x => x.product_id, "products", "id", onDelete: ReferentialAction.Cascade); });
        m.CreateTable("catalog_outbox", t => new { id = t.Column<Guid>("uuid", nullable: false), entity_type = t.Column<string>("character varying(50)", maxLength: 50, nullable: false), entity_id = t.Column<Guid>("uuid", nullable: false), operation = t.Column<string>("character varying(20)", maxLength: 20, nullable: false), created_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: false), processed_at = t.Column<DateTimeOffset>("timestamp with time zone", nullable: true), attempts = t.Column<int>("integer", nullable: false), last_error = t.Column<string>("text", nullable: true) }, c => c.PrimaryKey("PK_catalog_outbox", x => x.id));
        m.CreateIndex("IX_categories_slug", "categories", "slug", unique: true);
        m.CreateIndex("IX_products_slug", "products", "slug", unique: true);
        m.CreateIndex("IX_product_variants_sku", "product_variants", "sku", unique: true);
        m.CreateIndex("IX_catalog_outbox_pending", "catalog_outbox", new[] { "processed_at", "created_at" });
        m.CreateIndex("IX_products_category_id", "products", "category_id");
        m.CreateIndex("IX_product_variants_product_id", "product_variants", "product_id");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("catalog_outbox"); m.DropTable("product_variants"); m.DropTable("products"); m.DropTable("categories");
    }
}
