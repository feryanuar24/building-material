# Building Material API

Clean Architecture framework .NET 10:

- `Domain`: entities and roles.
- `Application`: request and response contracts.
- `Infrastructure`: EF Core PostgreSQL, hashing, JWT, SMTP, Elasticsearch, and Redis.
- `Api`: HTTP endpoints, cookie/JWT authentication, RBAC, rate limiting, and reCAPTCHA.

## Running

```bash
docker compose up --build
```

Swagger is available at `http://localhost:8080/swagger` in the Development environment. Mailpit is available at `http://localhost:8025`.

When the application starts, EF Core runs migrations and seeds data idempotently. The seed creates one admin and three customers. Logs are written to `logs/app.log` and remain available in the container filesystem.

## Catalog Search

The relational catalog uses three main entities:

- `categories`: hierarchical taxonomy (`parent_id`) with a unique slug.
- `products`: collection of materials that can be sold and belong to a single category.
- `product_variants`: SKUs that can be purchased with price, unit, and optional metric dimensions.

Catalog writes insert a transactional row into `catalog_outbox` in PostgreSQL. `CatalogSyncWorker` processes the outbox and upserts one Elasticsearch document for each active variant. Thus PostgreSQL remains the primary data source and the synchronization process can be safely replayed. The public homepage API reads catalog results directly from Elasticsearch:

- `GET /api/catalog` supports parameters `q`, `category`, `minPrice`, `maxPrice`, `diameterMm`, `thicknessMm`, `page`, and `pageSize`.
- `GET /api/catalog/categories` reads the category hierarchy from Redis with a ten‑minute cache.

Elasticsearch and Redis URLs can be configured via `Elasticsearch:Url`, `Elasticsearch:Index`, and `ConnectionStrings:Redis`.

## Authentication Endpoints

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/verify-email`
- `POST /api/auth/forgot-email`
- `POST /api/auth/reset-email`

Login returns a JWT and also sets an `access_token` cookie with `Secure`, `HttpOnly`, and `SameSite=Strict` attributes. The `Authorization: Bearer <token>` header is also supported.

`users` only store authentication credentials and metadata. Business data resides in `customers` and `admins`, with JWT roles `Customer`/`Admin`. Create admins via migrations or internal seed data, not through a public registration endpoint.

## Production

Replace secret via environment variables. When `ASPNETCORE_ENVIRONMENT=Production`, all authentication endpoints require a reCAPTCHA token verified with Google. The default authentication rate limit is 10 requests per minute per IP; adjust via `RateLimiting`.

## Testing

Run the authentication tests with:

```bash
dotnet test tests/BuildingMaterial.Api.Tests/BuildingMaterial.Api.Tests.csproj
```
