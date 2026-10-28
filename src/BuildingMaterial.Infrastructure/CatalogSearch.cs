using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingMaterial.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingMaterial.Infrastructure;

public sealed record CatalogDocument(Guid VariantId, Guid ProductId, string Sku, string Name, string Slug, string Description,
    string CategorySlug, string CategoryName, decimal Price, decimal? LengthMm, decimal? WidthMm, decimal? HeightMm,
    decimal? DiameterMm, decimal? ThicknessMm, decimal? WeightKg, string Unit);

public sealed class ElasticsearchCatalog(IHttpClientFactory clients, IConfiguration configuration, ILogger<ElasticsearchCatalog> logger)
{
    private readonly string index = configuration["Elasticsearch:Index"] ?? "catalog";
    private HttpClient Client => clients.CreateClient("elasticsearch");

    public async Task EnsureIndexAsync(CancellationToken ct)
    {
        using var response = await Client.PutAsync(index, JsonContent.Create(new
        {
            mappings = new { properties = new { name = new { type = "text" }, description = new { type = "text" }, categorySlug = new { type = "keyword" }, sku = new { type = "keyword" }, price = new { type = "double" }, lengthMm = new { type = "double" }, widthMm = new { type = "double" }, heightMm = new { type = "double" }, diameterMm = new { type = "double" }, thicknessMm = new { type = "double" }, weightKg = new { type = "double" } } }
        }), ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Conflict)
            logger.LogWarning("Elasticsearch index setup returned {StatusCode}", response.StatusCode);
    }

    public async Task UpsertAsync(CatalogDocument document, CancellationToken ct)
    {
        using var response = await Client.PutAsJsonAsync($"{index}/_doc/{document.VariantId}", document, cancellationToken: ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteProductAsync(Guid productId, CancellationToken ct)
    {
        using var response = await Client.PostAsJsonAsync($"{index}/_delete_by_query", new { query = new { term = new { productId = productId.ToString() } } }, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteVariantAsync(Guid variantId, CancellationToken ct)
    {
        using var response = await Client.DeleteAsync($"{index}/_doc/{variantId}", ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }
}

public sealed class CatalogSyncWorker(IServiceScopeFactory scopes, ElasticsearchCatalog search, ILogger<CatalogSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await search.EnsureIndexAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var messages = await db.CatalogOutboxMessages.Where(x => x.ProcessedAt == null).OrderBy(x => x.CreatedAt).Take(100).ToListAsync(stoppingToken);
                foreach (var message in messages)
                {
                    try
                    {
                        if (message.EntityType == "product" && message.Operation == "delete")
                            await search.DeleteProductAsync(message.EntityId, stoppingToken);
                        else if (message.EntityType == "variant" && message.Operation == "delete")
                            await search.DeleteVariantAsync(message.EntityId, stoppingToken);
                        else if (message.Operation != "delete")
                        {
                            foreach (var document in await LoadDocumentsAsync(db, message, stoppingToken))
                                await search.UpsertAsync(document, stoppingToken);
                        }
                        message.ProcessedAt = DateTimeOffset.UtcNow;
                    }
                    catch (Exception ex) { message.Attempts++; message.LastError = ex.Message; logger.LogError(ex, "Catalog sync failed for outbox {Id}", message.Id); }
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Catalog sync cycle failed"); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private static async Task<List<CatalogDocument>> LoadDocumentsAsync(AppDbContext db, CatalogOutboxMessage message, CancellationToken ct)
    {
        IQueryable<ProductVariant> variants = db.ProductVariants.Include(x => x.Product).ThenInclude(x => x.Category);
        if (message.EntityType == "variant") variants = variants.Where(x => x.Id == message.EntityId);
        else if (message.EntityType == "product") variants = variants.Where(x => x.ProductId == message.EntityId);
        else variants = variants.Where(x => x.Product.CategoryId == message.EntityId);
        return await variants.Where(x => x.IsActive && x.Product.IsActive).Select(x => new CatalogDocument(x.Id, x.ProductId, x.Sku, x.Product.Name, x.Product.Slug, x.Product.Description, x.Product.Category.Slug, x.Product.Category.Name, x.Price, x.LengthMm, x.WidthMm, x.HeightMm, x.DiameterMm, x.ThicknessMm, x.WeightKg, x.Unit)).ToListAsync(ct);
    }
}

public sealed class CategoryCache(IDistributedCache cache, AppDbContext db)
{
    public async Task<IReadOnlyList<CategoryDto>> GetAsync(CancellationToken ct)
    {
        const string key = "catalog:categories:v1";
        var cached = await cache.GetStringAsync(key, ct);
        if (cached is not null) return JsonSerializer.Deserialize<List<CategoryDto>>(cached) ?? [];
        var categories = await db.Categories.AsNoTracking().OrderBy(x => x.Name).Select(x => new CategoryDto(x.Id, x.Name, x.Slug, x.ParentId)).ToListAsync(ct);
        await cache.SetStringAsync(key, JsonSerializer.Serialize(categories), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }, ct);
        return categories;
    }
}

public sealed record CategoryDto(Guid Id, string Name, string Slug, Guid? ParentId);
