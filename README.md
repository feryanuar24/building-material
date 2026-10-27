# Building Material API

Kerangka Clean Architecture .NET 8:

- `Domain`: entitas dan role.
- `Application`: kontrak request/response.
- `Infrastructure`: EF Core PostgreSQL, hashing, JWT, SMTP.
- `Api`: endpoint HTTP, cookie/JWT authentication, RBAC, rate limiting, dan reCAPTCHA.

## Menjalankan

```bash
docker compose up --build
```

Swagger tersedia di `http://localhost:8080/swagger` saat environment Development. Mailpit tersedia di `http://localhost:8025`.

Saat aplikasi mulai, EF Core menjalankan migration dan seeder secara idempoten. Seeder
membuat satu admin dan tiga customer.
Error server dicatat melalui logging ASP.NET Core. Jika `SLACK_ERROR_WEBHOOK_URL`
diatur, error yang tidak tertangani juga dikirim ke Slack.
Untuk database PostgreSQL lama yang dibuat oleh init SQL, hapus volume
`postgres-data` sekali sebelum menjalankan versi migration ini.

Endpoint autentikasi:

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/auth/verify-email` (`email`, `token`)
- `POST /api/auth/forgot-email` (`email`; tidak memerlukan `token`)
- `POST /api/auth/reset-email` (`email`, `token`, `newPassword`)

Login mengembalikan JWT dan juga memasang cookie `access_token` dengan `Secure`, `HttpOnly`, dan `SameSite=Strict`. Header `Authorization: Bearer <token>` juga didukung.

`users` hanya menyimpan kredensial dan metadata autentikasi. Data bisnis berada di `customers` dan `admins`, dengan role JWT `Customer`/`Admin`. Buat admin melalui migrasi/seeder internal, bukan dari endpoint register publik.

## Production

Ganti `Jwt__Key`, password PostgreSQL, dan `Recaptcha__SecretKey` melalui secret manager/environment. Ketika `ASPNETCORE_ENVIRONMENT=Production`, semua endpoint autentikasi memerlukan token reCAPTCHA yang diverifikasi ke Google. Rate limit auth default adalah 10 request/menit per IP; sesuaikan melalui `RateLimiting`.
