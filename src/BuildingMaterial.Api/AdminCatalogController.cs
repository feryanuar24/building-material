using System.ComponentModel.DataAnnotations;
using BuildingMaterial.Domain;
using BuildingMaterial.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace BuildingMaterial.Api;

[ApiController]
[Route("api/admin/catalog")]
[Authorize(Roles = Roles.Admin)]
public sealed class AdminCatalogController(AppDbContext db, IDistributedCache cache) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<IReadOnlyList<CategoryResponse>> GetCategories(CancellationToken ct) =>
        await db.Categories.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new CategoryResponse(x.Id, x.Name, x.Slug, x.ParentId)).ToListAsync(ct);

    [HttpPost("categories")]
    public async Task<ActionResult<CategoryResponse>> CreateCategory(CreateCategoryRequest request, CancellationToken ct)
    {
        if (await db.Categories.AnyAsync(x => x.Slug == request.Slug, ct))
            return Conflict(new { message = "Category slug is already in use." });
        if (request.ParentId.HasValue && !await db.Categories.AnyAsync(x => x.Id == request.ParentId, ct))
            return BadRequest(new { message = "Parent category was not found." });

        var category = new Category { Name = request.Name, Slug = request.Slug, ParentId = request.ParentId };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync("catalog:categories:v1", ct);
        return CreatedAtAction(nameof(GetCategory), new { id = category.Id }, ToResponse(category));
    }

    [HttpGet("categories/{id:guid}", Name = nameof(GetCategory))]
    public async Task<ActionResult<CategoryResponse>> GetCategory(Guid id, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return category is null ? NotFound() : Ok(ToResponse(category));
    }

    [HttpPut("categories/{id:guid}")]
    public async Task<ActionResult<CategoryResponse>> UpdateCategory(Guid id, UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (category is null) return NotFound();
        if (await db.Categories.AnyAsync(x => x.Id != id && x.Slug == request.Slug, ct))
            return Conflict(new { message = "Category slug is already in use." });
        if (request.ParentId == id)
            return BadRequest(new { message = "A category cannot be its own parent." });
        if (request.ParentId.HasValue && !await db.Categories.AnyAsync(x => x.Id == request.ParentId, ct))
            return BadRequest(new { message = "Parent category was not found." });

        category.Name = request.Name;
        category.Slug = request.Slug;
        category.ParentId = request.ParentId;
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync("catalog:categories:v1", ct);
        return Ok(ToResponse(category));
    }

    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (category is null) return NotFound();
        if (await db.Categories.AnyAsync(x => x.ParentId == id, ct) || await db.Products.AnyAsync(x => x.CategoryId == id, ct))
            return Conflict(new { message = "Category cannot be deleted while it has children or products." });

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync("catalog:categories:v1", ct);
        return NoContent();
    }

    [HttpGet("products")]
    public async Task<IReadOnlyList<ProductResponse>> GetProducts(CancellationToken ct) =>
        await db.Products.AsNoTracking().OrderBy(x => x.Name)
            .Select(x => new ProductResponse(x.Id, x.CategoryId, x.Name, x.Slug, x.Description, x.IsActive,
                x.UpdatedAt, x.Variants.OrderBy(v => v.Sku).Select(v => new ProductVariantResponse(v.Id, v.ProductId, v.Sku,
                    v.Price, v.LengthMm, v.WidthMm, v.HeightMm, v.DiameterMm, v.ThicknessMm, v.WeightKg, v.Unit, v.IsActive)).ToList())).ToListAsync(ct);

    [HttpPost("products")]
    public async Task<ActionResult<ProductResponse>> CreateProduct(CreateProductRequest request, CancellationToken ct)
    {
        if (!await db.Categories.AnyAsync(x => x.Id == request.CategoryId, ct))
            return BadRequest(new { message = "Category was not found." });
        if (await db.Products.AnyAsync(x => x.Slug == request.Slug, ct))
            return Conflict(new { message = "Product slug is already in use." });

        var product = new Product
        {
            CategoryId = request.CategoryId, Name = request.Name, Slug = request.Slug,
            Description = request.Description, IsActive = request.IsActive
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, await ProductResponseFor(product.Id, ct));
    }

    [HttpGet("products/{id:guid}", Name = nameof(GetProduct))]
    public async Task<ActionResult<ProductResponse>> GetProduct(Guid id, CancellationToken ct)
    {
        var product = await ProductResponseFor(id, ct);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPut("products/{id:guid}")]
    public async Task<ActionResult<ProductResponse>> UpdateProduct(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return NotFound();
        if (!await db.Categories.AnyAsync(x => x.Id == request.CategoryId, ct))
            return BadRequest(new { message = "Category was not found." });
        if (await db.Products.AnyAsync(x => x.Id != id && x.Slug == request.Slug, ct))
            return Conflict(new { message = "Product slug is already in use." });

        product.CategoryId = request.CategoryId;
        product.Name = request.Name;
        product.Slug = request.Slug;
        product.Description = request.Description;
        product.IsActive = request.IsActive;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(await ProductResponseFor(id, ct));
    }

    [HttpDelete("products/{id:guid}")]
    public async Task<IActionResult> DeleteProduct(Guid id, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (product is null) return NotFound();
        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("products/{productId:guid}/variants")]
    public async Task<ActionResult<ProductVariantResponse>> CreateVariant(Guid productId, CreateProductVariantRequest request, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(x => x.Id == productId, ct)) return NotFound();
        if (await db.ProductVariants.AnyAsync(x => x.Sku == request.Sku, ct))
            return Conflict(new { message = "SKU is already in use." });

        var variant = new ProductVariant
        {
            ProductId = productId, Sku = request.Sku, Price = request.Price, LengthMm = request.LengthMm,
            WidthMm = request.WidthMm, HeightMm = request.HeightMm, DiameterMm = request.DiameterMm,
            ThicknessMm = request.ThicknessMm, WeightKg = request.WeightKg, Unit = request.Unit, IsActive = request.IsActive
        };
        db.ProductVariants.Add(variant);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetVariant), new { id = variant.Id }, ToResponse(variant));
    }

    [HttpGet("variants/{id:guid}", Name = nameof(GetVariant))]
    public async Task<ActionResult<ProductVariantResponse>> GetVariant(Guid id, CancellationToken ct)
    {
        var variant = await db.ProductVariants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return variant is null ? NotFound() : Ok(ToResponse(variant));
    }

    [HttpPut("variants/{id:guid}")]
    public async Task<ActionResult<ProductVariantResponse>> UpdateVariant(Guid id, UpdateProductVariantRequest request, CancellationToken ct)
    {
        var variant = await db.ProductVariants.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (variant is null) return NotFound();
        if (await db.ProductVariants.AnyAsync(x => x.Id != id && x.Sku == request.Sku, ct))
            return Conflict(new { message = "SKU is already in use." });

        variant.Sku = request.Sku; variant.Price = request.Price; variant.LengthMm = request.LengthMm;
        variant.WidthMm = request.WidthMm; variant.HeightMm = request.HeightMm; variant.DiameterMm = request.DiameterMm;
        variant.ThicknessMm = request.ThicknessMm; variant.WeightKg = request.WeightKg;
        variant.Unit = request.Unit; variant.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return Ok(ToResponse(variant));
    }

    [HttpDelete("variants/{id:guid}")]
    public async Task<IActionResult> DeleteVariant(Guid id, CancellationToken ct)
    {
        var variant = await db.ProductVariants.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (variant is null) return NotFound();
        db.ProductVariants.Remove(variant);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ProductResponse?> ProductResponseFor(Guid id, CancellationToken ct) =>
        await db.Products.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new ProductResponse(x.Id, x.CategoryId, x.Name, x.Slug, x.Description, x.IsActive,
                x.UpdatedAt, x.Variants.OrderBy(v => v.Sku).Select(v => new ProductVariantResponse(v.Id, v.ProductId, v.Sku,
                    v.Price, v.LengthMm, v.WidthMm, v.HeightMm, v.DiameterMm, v.ThicknessMm, v.WeightKg, v.Unit, v.IsActive)).ToList())).SingleOrDefaultAsync(ct);

    private static CategoryResponse ToResponse(Category x) => new(x.Id, x.Name, x.Slug, x.ParentId);
    private static ProductVariantResponse ToResponse(ProductVariant x) =>
        new(x.Id, x.ProductId, x.Sku, x.Price, x.LengthMm, x.WidthMm, x.HeightMm, x.DiameterMm, x.ThicknessMm, x.WeightKg, x.Unit, x.IsActive);
    private static ProductVariantResponse ToVariantResponse(ProductVariant x) => ToResponse(x);
}

public sealed record CategoryResponse(Guid Id, string Name, string Slug, Guid? ParentId);

public sealed record CreateCategoryRequest
{
    [Required, MaxLength(200)] public required string Name { get; init; }
    [Required, MaxLength(220)] public required string Slug { get; init; }
    public Guid? ParentId { get; init; }
}

public sealed record UpdateCategoryRequest
{
    [Required, MaxLength(200)] public required string Name { get; init; }
    [Required, MaxLength(220)] public required string Slug { get; init; }
    public Guid? ParentId { get; init; }
}

public sealed record ProductResponse(Guid Id, Guid CategoryId, string Name, string Slug, string Description, bool IsActive,
    DateTimeOffset UpdatedAt, IReadOnlyList<ProductVariantResponse> Variants);

public sealed record CreateProductRequest
{
    [Required] public Guid CategoryId { get; init; }
    [Required, MaxLength(300)] public required string Name { get; init; }
    [Required, MaxLength(320)] public required string Slug { get; init; }
    [Required] public required string Description { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record UpdateProductRequest
{
    [Required] public Guid CategoryId { get; init; }
    [Required, MaxLength(300)] public required string Name { get; init; }
    [Required, MaxLength(320)] public required string Slug { get; init; }
    [Required] public required string Description { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record ProductVariantResponse(Guid Id, Guid ProductId, string Sku, decimal Price, decimal? LengthMm,
    decimal? WidthMm, decimal? HeightMm, decimal? DiameterMm, decimal? ThicknessMm, decimal? WeightKg, string Unit, bool IsActive);

public sealed record CreateProductVariantRequest
{
    [Required, MaxLength(100)] public required string Sku { get; init; }
    [Range(0, double.MaxValue)] public decimal Price { get; init; }
    public decimal? LengthMm { get; init; }
    public decimal? WidthMm { get; init; }
    public decimal? HeightMm { get; init; }
    public decimal? DiameterMm { get; init; }
    public decimal? ThicknessMm { get; init; }
    public decimal? WeightKg { get; init; }
    [Required, MaxLength(30)] public required string Unit { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record UpdateProductVariantRequest
{
    [Required, MaxLength(100)] public required string Sku { get; init; }
    [Range(0, double.MaxValue)] public decimal Price { get; init; }
    public decimal? LengthMm { get; init; }
    public decimal? WidthMm { get; init; }
    public decimal? HeightMm { get; init; }
    public decimal? DiameterMm { get; init; }
    public decimal? ThicknessMm { get; init; }
    public decimal? WeightKg { get; init; }
    [Required, MaxLength(30)] public required string Unit { get; init; }
    public bool IsActive { get; init; } = true;
}
