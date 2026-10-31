using BuildingMaterial.Api;
using BuildingMaterial.Domain;
using BuildingMaterial.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace BuildingMaterial.Api.Tests;

public sealed class AdminCatalogControllerTests
{
    [Fact]
    public void Controller_requires_admin_role()
    {
        var attribute = typeof(AdminCatalogController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single();

        Assert.Equal(Roles.Admin, attribute.Roles);
    }

    [Fact]
    public async Task CreateCategory_creates_category_and_returns_created_response()
    {
        await using var db = CreateDb();
        var controller = CreateController(db);

        var result = await controller.CreateCategory(
            new CreateCategoryRequest { Name = "Cement", Slug = "cement" },
            CancellationToken.None);

        var response = Assert.IsType<CreatedAtActionResult>(result.Result);
        var category = Assert.IsType<CategoryResponse>(response.Value);
        Assert.Equal("cement", category.Slug);
        Assert.Equal(category.Id, (await db.Categories.SingleAsync()).Id);
    }

    [Fact]
    public async Task CreateCategory_returns_conflict_for_duplicate_slug()
    {
        await using var db = CreateDb();
        db.Categories.Add(new Category { Name = "Cement", Slug = "cement" });
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.CreateCategory(
            new CreateCategoryRequest { Name = "Another Cement", Slug = "cement" },
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Single(db.Categories);
    }

    [Fact]
    public async Task CreateProduct_and_variant_returns_created_resources()
    {
        await using var db = CreateDb();
        var category = new Category { Name = "Cement", Slug = "cement" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var productResult = await controller.CreateProduct(
            new CreateProductRequest
            {
                CategoryId = category.Id,
                Name = "Portland Cement",
                Slug = "portland-cement",
                Description = "General-purpose cement"
            },
            CancellationToken.None);

        var productResponse = Assert.IsType<CreatedAtActionResult>(productResult.Result);
        var product = Assert.IsType<ProductResponse>(productResponse.Value);
        var variantResult = await controller.CreateVariant(
            product.Id,
            new CreateProductVariantRequest
            {
                Sku = "CEMENT-001",
                Price = 85000,
                Unit = "bag"
            },
            CancellationToken.None);

        var variantResponse = Assert.IsType<CreatedAtActionResult>(variantResult.Result);
        var variant = Assert.IsType<ProductVariantResponse>(variantResponse.Value);
        Assert.Equal(product.Id, variant.ProductId);
        Assert.Equal(85000, variant.Price);
        Assert.Equal(product.Id, (await db.Products.Include(x => x.Variants).SingleAsync()).Id);
        Assert.Single(db.ProductVariants);
    }

    [Fact]
    public async Task DeleteCategory_returns_conflict_when_products_exist()
    {
        await using var db = CreateDb();
        var category = new Category { Name = "Cement", Slug = "cement" };
        db.Categories.Add(category);
        db.Products.Add(new Product
        {
            Category = category,
            Name = "Portland Cement",
            Slug = "portland-cement",
            Description = "General-purpose cement"
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db);

        var result = await controller.DeleteCategory(category.Id, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Single(db.Categories);
    }

    [Fact]
    public async Task UpdateVariant_returns_conflict_for_duplicate_sku()
    {
        await using var db = CreateDb();
        var product = new Product { Name = "Product", Slug = "product", Description = "Description" };
        db.Products.Add(product);
        db.ProductVariants.AddRange(
            new ProductVariant { Product = product, Sku = "SKU-1", Unit = "piece" },
            new ProductVariant { Product = product, Sku = "SKU-2", Unit = "piece" });
        await db.SaveChangesAsync();
        var variants = await db.ProductVariants.OrderBy(x => x.Sku).ToListAsync();
        var controller = CreateController(db);

        var result = await controller.UpdateVariant(
            variants[1].Id,
            new UpdateProductVariantRequest { Sku = "SKU-1", Unit = "piece" },
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("SKU-2", (await db.ProductVariants.FindAsync(variants[1].Id))!.Sku);
    }

    private static AdminCatalogController CreateController(AppDbContext db) =>
        new(db, new NoOpDistributedCache());

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private sealed class NoOpDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
    }
}
