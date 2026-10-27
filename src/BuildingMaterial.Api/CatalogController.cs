using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using BuildingMaterial.Infrastructure;

namespace BuildingMaterial.Api;

[ApiController]
[Route("api/catalog")]
public sealed class CatalogController(CategoryCache categories, IHttpClientFactory clients, IConfiguration configuration) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken ct) => await categories.GetAsync(ct);

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] string? category, [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice, [FromQuery] decimal? diameterMm, [FromQuery] decimal? thicknessMm,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 24, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, 10000); pageSize = Math.Clamp(pageSize, 1, 100);
        var filters = new JsonArray();
        if (!string.IsNullOrWhiteSpace(category)) filters.Add(new JsonObject { ["term"] = new JsonObject { ["categorySlug"] = category } });
        if (minPrice.HasValue || maxPrice.HasValue || diameterMm.HasValue || thicknessMm.HasValue)
        {
            var range = new JsonObject();
            if (minPrice.HasValue || maxPrice.HasValue) range["price"] = Range(minPrice, maxPrice);
            if (diameterMm.HasValue) range["diameterMm"] = new JsonObject { ["gte"] = diameterMm.Value, ["lte"] = diameterMm.Value };
            if (thicknessMm.HasValue) range["thicknessMm"] = new JsonObject { ["gte"] = thicknessMm.Value, ["lte"] = thicknessMm.Value };
            foreach (var item in range) filters.Add(new JsonObject { ["range"] = new JsonObject { [item.Key] = item.Value?.DeepClone() } });
        }
        JsonNode query = string.IsNullOrWhiteSpace(q)
            ? new JsonObject { ["bool"] = new JsonObject { ["filter"] = filters } }
            : new JsonObject { ["bool"] = new JsonObject { ["must"] = new JsonArray { new JsonObject { ["multi_match"] = new JsonObject { ["query"] = q, ["fields"] = new JsonArray(JsonValue.Create("name^3"), JsonValue.Create("sku^2"), JsonValue.Create("description"), JsonValue.Create("categoryName")) } } }, ["filter"] = filters } };
        var body = new JsonObject { ["from"] = (page - 1) * pageSize, ["size"] = pageSize, ["query"] = query, ["sort"] = new JsonArray(JsonValue.Create("_score"), new JsonObject { ["name.keyword"] = "asc" }) };
        var client = clients.CreateClient("elasticsearch");
        using var response = await client.PostAsJsonAsync($"{configuration["Elasticsearch:Index"] ?? "catalog"}/_search", body, ct);
        response.EnsureSuccessStatusCode();
        return Content(await response.Content.ReadAsStringAsync(ct), "application/json");
    }

    private static JsonObject Range(decimal? min, decimal? max)
    {
        var value = new JsonObject();
        if (min.HasValue) value["gte"] = min.Value;
        if (max.HasValue) value["lte"] = max.Value;
        return value;
    }
}
