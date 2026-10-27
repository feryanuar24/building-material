# Building Material API

Kerangka Clean Architecture .NET 8:

- `Domain`: entitas dan peran.
- `Application`: kontrak permintaan dan respons.
- `Infrastructure`: EF Core PostgreSQL, hashing, JWT, SMTP, Elasticsearch, dan Redis.
- `Api`: endpoint HTTP, autentikasi cookie/JWT, RBAC, pembatasan laju, dan reCAPTCHA.

## Menjalankan

```bash
docker compose up --build
```

Swagger tersedia di `http://localhost:8080/swagger` pada lingkungan Development.
Mailpit tersedia di `http://localhost:8025`.

Saat aplikasi mulai, EF Core menjalankan migrasi dan pengisi data secara
idempoten. Pengisi data membuat satu admin dan tiga customer. Error server
dicatat melalui logging ASP.NET Core. Jika `SLACK_ERROR_WEBHOOK_URL` diatur,
error yang tidak tertangani juga dikirim ke Slack.

Untuk database PostgreSQL lama yang dibuat oleh SQL inisialisasi, hapus volume
`postgres-data` sekali sebelum menjalankan versi migrasi ini.

## Pencarian katalog

Katalog relasional menggunakan tiga entitas utama:

- `categories`: taksonomi hierarkis (`parent_id`) dengan slug unik.
- `products`: kelompok material yang dapat dijual dan dimiliki oleh satu kategori.
- `product_variants`: SKU yang dapat dibeli dengan harga, satuan, dan dimensi
  metrik opsional (`length_mm`, `width_mm`, `height_mm`, `diameter_mm`,
  `thickness_mm`, dan `weight_kg`). Dengan struktur ini, dimensi pipa dan besi
  dapat dicari tanpa harus menyisipkannya ke dalam nama produk.

Penulisan katalog memasukkan satu baris transaksional ke `catalog_outbox` di
PostgreSQL. `CatalogSyncWorker` memproses outbox tersebut dan melakukan upsert
satu dokumen Elasticsearch untuk setiap varian aktif. Dengan demikian,
PostgreSQL tetap menjadi sumber data utama dan proses sinkronisasi dapat
diulang dengan aman. API homepage publik membaca hasil katalog langsung dari
Elasticsearch:

- `GET /api/catalog` mendukung parameter `q`, `category`, `minPrice`,
  `maxPrice`, `diameterMm`, `thicknessMm`, `page`, dan `pageSize`.
- `GET /api/catalog/categories` membaca struktur kategori dari Redis dengan
  cache selama sepuluh menit.

URL Elasticsearch dan Redis dapat dikonfigurasi melalui `Elasticsearch:Url`,
`Elasticsearch:Index`, dan `ConnectionStrings:Redis`.

## Endpoint autentikasi

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/verify-email` (`email`, `token`)
- `POST /api/auth/forgot-email` (`email`; tidak memerlukan `token`)
- `POST /api/auth/reset-email` (`email`, `token`, `newPassword`)

Login mengembalikan JWT dan juga memasang cookie `access_token` dengan atribut
`Secure`, `HttpOnly`, dan `SameSite=Strict`. Header `Authorization: Bearer
<token>` juga didukung.

`users` hanya menyimpan kredensial dan metadata autentikasi. Data bisnis berada
di `customers` dan `admins`, dengan peran JWT `Customer`/`Admin`. Buat admin
melalui migrasi atau pengisi data internal, bukan dari endpoint pendaftaran publik.

## Produksi

Ganti `Jwt__Key`, password PostgreSQL, dan `Recaptcha__SecretKey` melalui
secret manager atau environment variable. Ketika
`ASPNETCORE_ENVIRONMENT=Production`, semua endpoint autentikasi memerlukan
token reCAPTCHA yang diverifikasi ke Google. Batas laju autentikasi default
adalah 10 permintaan per menit per IP; sesuaikan melalui `RateLimiting`.
