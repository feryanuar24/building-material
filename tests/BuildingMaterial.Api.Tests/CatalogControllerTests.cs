using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BuildingMaterial.Api;
using BuildingMaterial.Domain;
using BuildingMaterial.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;

namespace BuildingMaterial.Api.Tests;

public sealed class CatalogControllerTests
{
    [Fact]
    public async Task Categories_returns_categories_from_database_and_caches_result()
    {
        await using var db = CreateDb();
        db.Categories.AddRange(
            new Category { Name = "Pasir & Semen", Slug = "pasir-semen" },
            new Category { Name = "Besi & Baja", Slug = "besi-baja" });
        await db.SaveChangesAsync();

        var cache = new InMemoryDistributedCache();
        var categoryCache = new CategoryCache(cache, db);
        var controller = CreateController(categoryCache);

        var result = await controller.Categories(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Besi & Baja", result[0].Name);
        Assert.Equal("Pasir & Semen", result[1].Name);

        // Verify it was saved to cache
        var cachedBytes = await cache.GetAsync("catalog:categories:v1");
        Assert.NotNull(cachedBytes);
        var cachedCategories = JsonSerializer.Deserialize<List<CategoryDto>>(cachedBytes);
        Assert.NotNull(cachedCategories);
        Assert.Equal(2, cachedCategories.Count);
    }

    [Fact]
    public async Task Categories_returns_cached_categories_when_available()
    {
        await using var db = CreateDb();
        var cache = new InMemoryDistributedCache();
        var cachedData = new List<CategoryDto>
        {
            new(Guid.NewGuid(), "Cached Category", "cached-category", null)
        };
        await cache.SetStringAsync("catalog:categories:v1", JsonSerializer.Serialize(cachedData));

        var categoryCache = new CategoryCache(cache, db);
        var controller = CreateController(categoryCache);

        var result = await controller.Categories(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Cached Category", result[0].Name);
        Assert.Empty(db.Categories); // Database was never queried
    }

    [Fact]
    public async Task Search_default_parameters_generates_correct_elasticsearch_query()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            capturedRequest = req;
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"hits\":{\"total\":{\"value\":0},\"hits\":[]}}", Encoding.UTF8, "application/json")
            };
        });

        var controller = CreateController(httpHandler: handler);

        var response = await controller.Search(
            q: null,
            category: null,
            minPrice: null,
            maxPrice: null,
            diameterMm: null,
            thicknessMm: null,
            page: 1,
            pageSize: 24,
            CancellationToken.None);

        var contentResult = Assert.IsType<ContentResult>(response);
        Assert.Equal("application/json", contentResult.ContentType);
        Assert.NotNull(capturedRequest);
        Assert.Equal("/catalog/_search", capturedRequest.RequestUri?.PathAndQuery);

        var json = JsonNode.Parse(capturedBody!);
        Assert.NotNull(json);
        Assert.Equal(0, json["from"]?.GetValue<int>());
        Assert.Equal(24, json["size"]?.GetValue<int>());

        // Query bool should contain empty filter array and no "must"
        var boolQuery = json["query"]?["bool"];
        Assert.NotNull(boolQuery);
        Assert.Null(boolQuery["must"]);
        var filters = boolQuery["filter"]?.AsArray();
        Assert.NotNull(filters);
        Assert.Empty(filters);

        // Sort check
        var sorts = json["sort"]?.AsArray();
        Assert.NotNull(sorts);
        Assert.Equal(2, sorts.Count);
        Assert.Equal("_score", sorts[0]?.GetValue<string>());
        Assert.Equal("asc", sorts[1]?["name.keyword"]?.GetValue<string>());
    }

    [Fact]
    public async Task Search_with_keyword_generates_multi_match_query()
    {
        string? capturedBody = null;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"hits\":{\"hits\":[]}}", Encoding.UTF8, "application/json")
            };
        });

        var controller = CreateController(httpHandler: handler);

        await controller.Search(
            q: "semen gresik",
            category: null,
            minPrice: null,
            maxPrice: null,
            diameterMm: null,
            thicknessMm: null,
            page: 1,
            pageSize: 24,
            CancellationToken.None);

        var json = JsonNode.Parse(capturedBody!);
        Assert.NotNull(json);

        var must = json["query"]?["bool"]?["must"]?.AsArray();
        Assert.NotNull(must);
        Assert.Single(must);

        var multiMatch = must[0]?["multi_match"];
        Assert.NotNull(multiMatch);
        Assert.Equal("semen gresik", multiMatch["query"]?.GetValue<string>());

        var fields = multiMatch["fields"]?.AsArray().Select(x => x?.GetValue<string>()).ToList();
        Assert.NotNull(fields);
        Assert.Equal(["name^3", "sku^2", "description", "categoryName"], fields);
    }

    [Fact]
    public async Task Search_with_category_adds_term_filter()
    {
        string? capturedBody = null;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var controller = CreateController(httpHandler: handler);

        await controller.Search(
            q: null,
            category: "besi-beton",
            minPrice: null,
            maxPrice: null,
            diameterMm: null,
            thicknessMm: null,
            page: 1,
            pageSize: 24,
            CancellationToken.None);

        var json = JsonNode.Parse(capturedBody!);
        Assert.NotNull(json);

        var filters = json["query"]?["bool"]?["filter"]?.AsArray();
        Assert.NotNull(filters);
        Assert.Single(filters);

        var term = filters[0]?["term"]?["categorySlug"]?.GetValue<string>();
        Assert.Equal("besi-beton", term);
    }

    [Fact]
    public async Task Search_with_price_range_and_dimensions_adds_all_range_filters()
    {
        string? capturedBody = null;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var controller = CreateController(httpHandler: handler);

        await controller.Search(
            q: "besi",
            category: "besi-beton",
            minPrice: 50000m,
            maxPrice: 150000m,
            diameterMm: 10m,
            thicknessMm: 2.5m,
            page: 1,
            pageSize: 10,
            CancellationToken.None);

        var json = JsonNode.Parse(capturedBody!);
        Assert.NotNull(json);

        var filters = json["query"]?["bool"]?["filter"]?.AsArray();
        Assert.NotNull(filters);
        // Category term + 3 ranges (price, diameterMm, thicknessMm)
        Assert.Equal(4, filters.Count);

        // Price range
        var priceRange = filters.FirstOrDefault(f => f?["range"]?["price"] is not null)?["range"]?["price"];
        Assert.NotNull(priceRange);
        Assert.Equal(50000m, priceRange["gte"]?.GetValue<decimal>());
        Assert.Equal(150000m, priceRange["lte"]?.GetValue<decimal>());

        // Diameter range
        var diameterRange = filters.FirstOrDefault(f => f?["range"]?["diameterMm"] is not null)?["range"]?["diameterMm"];
        Assert.NotNull(diameterRange);
        Assert.Equal(10m, diameterRange["gte"]?.GetValue<decimal>());
        Assert.Equal(10m, diameterRange["lte"]?.GetValue<decimal>());

        // Thickness range
        var thicknessRange = filters.FirstOrDefault(f => f?["range"]?["thicknessMm"] is not null)?["range"]?["thicknessMm"];
        Assert.NotNull(thicknessRange);
        Assert.Equal(2.5m, thicknessRange["gte"]?.GetValue<decimal>());
        Assert.Equal(2.5m, thicknessRange["lte"]?.GetValue<decimal>());
    }

    [Fact]
    public async Task Search_with_only_minPrice_or_only_maxPrice_generates_partial_range()
    {
        string? capturedBody = null;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var controller = CreateController(httpHandler: handler);

        // Test only minPrice
        await controller.Search(
            q: null, category: null, minPrice: 20000m, maxPrice: null,
            diameterMm: null, thicknessMm: null, page: 1, pageSize: 10, CancellationToken.None);

        var json = JsonNode.Parse(capturedBody!);
        var priceRange = json?["query"]?["bool"]?["filter"]?[0]?["range"]?["price"];
        Assert.NotNull(priceRange);
        Assert.Equal(20000m, priceRange["gte"]?.GetValue<decimal>());
        Assert.Null(priceRange["lte"]);

        // Test only maxPrice
        await controller.Search(
            q: null, category: null, minPrice: null, maxPrice: 75000m,
            diameterMm: null, thicknessMm: null, page: 1, pageSize: 10, CancellationToken.None);

        json = JsonNode.Parse(capturedBody!);
        priceRange = json?["query"]?["bool"]?["filter"]?[0]?["range"]?["price"];
        Assert.NotNull(priceRange);
        Assert.Null(priceRange["gte"]);
        Assert.Equal(75000m, priceRange["lte"]?.GetValue<decimal>());
    }

    [Theory]
    [InlineData(0, 10, 0, 10)] // page 0 clamped to 1 -> (1-1)*10 = from 0
    [InlineData(1, 10, 0, 10)] // page 1 -> (1-1)*10 = from 0
    [InlineData(3, 15, 30, 15)] // page 3 -> (3-1)*15 = from 30
    [InlineData(-10, 200, 0, 100)] // page -10 clamped to 1, pageSize 200 clamped to 100
    [InlineData(50000, 0, (10000 - 1) * 1, 1)] // page clamped to 10000, pageSize 0 clamped to 1
    public async Task Search_clamps_page_and_pageSize_properly(int inputPage, int inputPageSize, int expectedFrom, int expectedSize)
    {
        string? capturedBody = null;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var controller = CreateController(httpHandler: handler);

        await controller.Search(
            q: null, category: null, minPrice: null, maxPrice: null,
            diameterMm: null, thicknessMm: null, page: inputPage, pageSize: inputPageSize, CancellationToken.None);

        var json = JsonNode.Parse(capturedBody!);
        Assert.NotNull(json);
        Assert.Equal(expectedFrom, json["from"]?.GetValue<int>());
        Assert.Equal(expectedSize, json["size"]?.GetValue<int>());
    }

    [Fact]
    public async Task Search_uses_elasticsearch_index_from_configuration()
    {
        HttpRequestMessage? capturedRequest = null;

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            capturedRequest = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elasticsearch:Index"] = "custom-building-materials"
            })
            .Build();

        var controller = CreateController(httpHandler: handler, configuration: config);

        await controller.Search(
            q: null, category: null, minPrice: null, maxPrice: null,
            diameterMm: null, thicknessMm: null, page: 1, pageSize: 24, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal("/custom-building-materials/_search", capturedRequest.RequestUri?.PathAndQuery);
    }

    private static CatalogController CreateController(
        CategoryCache? categoryCache = null,
        HttpMessageHandler? httpHandler = null,
        IConfiguration? configuration = null)
    {
        var db = CreateDb();
        var cache = new InMemoryDistributedCache();
        categoryCache ??= new CategoryCache(cache, db);
        var handler = httpHandler ?? new MockHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            }));
        var clientFactory = new TestHttpClientFactory(handler);
        configuration ??= new ConfigurationBuilder().Build();

        return new CatalogController(categoryCache, clientFactory, configuration);
    }

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler)
        {
            BaseAddress = new Uri("http://localhost:9200/")
        };
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class InMemoryDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public byte[]? Get(string key) => _store.GetValueOrDefault(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
