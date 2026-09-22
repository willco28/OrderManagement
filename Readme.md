# Order Management API

REST API untuk order management dengan penanganan **concurrency**, **idempotency**, dan **production-ready logging**. Dibuat sebagai submission untuk **test teknis Senior .NET Developer di ASTEK (Adaya Solusi Teknologi)**.

---

## 📋 Daftar Isi

- [Fitur Utama](#fitur-utama)
- [Teknologi](#teknologi)
- [Arsitektur](#arsitektur)
- [Cara Menjalankan](#cara-menjalankan)
- [Endpoint API](#endpoint-api)
- [Strategi Concurrency](#strategi-concurrency)
- [Strategi Idempotency](#strategi-idempotency)
- [Race Condition Lain yang Dicegah](#race-condition-lain-yang-dicegah)
- [Logging & Tracing](#logging--tracing)
- [Error Handling](#error-handling)
- [Testing](#testing)
- [Skema Database](#skema-database)
- [Keputusan Desain](#keputusan-desain)
- [Catatan Testing](#catatan-testing)
- [Struktur Proyek](#struktur-proyek)

---

## ✨ Fitur Utama

- **Create Order** — membuat order dengan customerId, list of items, shippingAddress.
- **Get Order** — detail order berdasarkan ID.
- **List Orders** — daftar order dengan filter (status, customerId, date range) dan pagination.
- **Update Order Status** — update status dengan validasi transisi.
- **Cancel Order** — cancel order + restore stock.
- **Idempotency** — mencegah double order via header `Idempotency-Key`.
- **Concurrency-safe stock** — stok tidak pernah minus, bahkan saat concurrent request.
- **Optimistic status transition** — hanya satu admin yang menang saat update bersamaan.
- **Structured logging** — dengan correlation ID untuk tracing antar log.

---

## 🛠️ Teknologi

| Komponen | Pilihan | Alasan |
|---|---|---|
| Framework | ASP.NET Core **.NET 10** | LTS terbaru, performa tinggi |
| Database | **PostgreSQL 16** | Row-level locking untuk concurrency, gratis, ringan untuk Docker |
| ORM | Entity Framework Core 10 | Produktivitas, migration, LINQ |
| Logging | **Serilog** | Structured logging, multiple sinks, correlation ID |
| Validation | FluentValidation + SharpGrip | Auto-validation MVC, pengganti FluentValidation.AspNetCore yang deprecated |
| API Docs | Swashbuckle (Swagger) | Standar industri |
| Testing | xUnit v3 + Testcontainers | Integration test dengan PostgreSQL nyata |
| Container | Docker + Docker Compose | Environment konsisten, mudah dijalankan |

---

## 🏗️ Arsitektur

Aplikasi memakai **Clean Architecture sederhana** dengan 3 layer:
┌──────────────────────────────────────────┐
│ OrderManagement.API │ ← HTTP layer
│ Controllers, Middleware, DTOs, │
│ Services, Validators │
└────────────────┬─────────────────────────┘
│
┌────────────────▼─────────────────────────┐
│ OrderManagement.Infrastructure │ ← Data layer
│ DbContext, EF Config, Repositories │
└────────────────┬─────────────────────────┘
│
┌────────────────▼─────────────────────────┐
│ OrderManagement.Domain │ ← Business rules
│ Entities, Enums, Transitions │
└──────────────────────────────────────────┘


**Aturan dependency:**
- `Domain` — tidak bergantung ke siapa pun.
- `Infrastructure` — bergantung ke `Domain`.
- `API` — bergantung ke `Domain` + `Infrastructure`.

---

## 🚀 Cara Menjalankan

### Prasyarat

- **Docker Desktop** (Windows/Mac) atau Docker Engine + Compose (Linux)
- **.NET 10 SDK** (hanya jika ingin menjalankan di luar Docker)

### Menjalankan via Docker Compose (direkomendasikan)

```bash
# Clone repo
git clone <url-repo>
cd OrderManagement

# Jalankan di background
docker compose up -d --build

# Cek status
docker compose ps

# Lihat log
docker compose logs api --tail 50

# Setelah container berjalan:
Swagger UI: http://localhost:5000/swagger
API base URL: http://localhost:5000/api

# Stop & Cleanup
## Hentikan container (data DB tetap tersimpan di volume)
docker compose down

## Hentikan + hapus volume DB (fresh start)
docker compose down -v

# Menjalankan di Luar Docker (untuk development)
## 1. Start PostgreSQL saja
docker compose up -d db

## 2. Ubah connection string di appsettings.Development.json
##    menjadi: Host=localhost;Port=5432;Database=orderdb;Username=postgres;Password=postgres

## 3. Jalankan API
dotnet run --project OrderManagement.API

📡 Endpoint API
Base URL: http://localhost:5000/api

1. Create Order
POST /api/orders
Content-Type: application/json
Idempotency-Key: <opsional — lihat bagian Idempotency>

Request body:
{
  "customerId": "11111111-1111-1111-1111-111111111111",
  "shippingAddress": "Jl. Sudirman No. 1, Jakarta",
  "items": [
    { "productId": "<uuid>", "quantity": 5 }
  ]
}

Response 201 Created:
{
  "id": "<order-id>",
  "customerId": "11111111-1111-1111-1111-111111111111",
  "shippingAddress": "Jl. Sudirman No. 1, Jakarta",
  "status": "Pending",
  "createdAt": "2026-01-15T10:30:00Z",
  "items": [
    { "productId": "<uuid>", "quantity": 5, "unitPrice": 50000 }
  ]
}

Error responses:
400 — validasi gagal
422 — stok tidak cukup

2. Get Order
GET /api/orders/{id}
Response: 200 OK dengan detail order, atau 404 Not Found.

3. List Orders
GET /api/orders?status=Confirmed&customerId=<uuid>&from=2026-01-01&to=2026-12-31&page=1&pageSize=20
Semua parameter opsional.

Response:
{
  "items": [ ... ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 20,
  "totalPages": 3
}

4. Update Order Status
PATCH /api/orders/{id}/status
Content-Type: application/json

Request body:
{ "status": "Confirmed" }

Transisi yang valid:

Dari	Ke	Valid?
Pending	Confirmed	✅
Pending	Cancelled	✅
Confirmed	Shipped	✅
Confirmed	Cancelled	✅
Shipped	Delivered	✅
Delivered	(apa pun)	❌ terminal
Cancelled	(apa pun)	❌ terminal
Response: 200 OK jika valid, 409 Conflict jika transisi tidak valid.

5. Cancel Order
POST /api/orders/{id}/cancel

Hanya bisa dilakukan jika status Pending atau Confirmed. Saat cancel, stock dikembalikan.
Response: 200 OK atau 409 Conflict.

🔒 Strategi Concurrency
Soal meminta penanganan 3 skenario concurrency. Berikut strategi yang dipilih.

Skenario A — Concurrent Stock Deduction
Masalah: Dua user submit order yang sama-sama butuh 10 unit Product X, stok tinggal 15. Tidak boleh total terdeduksi > 15.

Solusi: Atomic SQL UPDATE dengan conditional WHERE.
UPDATE "Products"
SET "StockQuantity" = "StockQuantity" - @qty
WHERE "Id" = @productId AND "StockQuantity" >= @qty

Cara kerja:

PostgreSQL memberi row-level lock pada baris Products yang di-update.
Request kedua menunggu lock dilepas.
Request pertama: kondisi StockQuantity >= qty terpenuhi → update → commit.
Request kedua: kondisi tidak terpenuhi (stok sudah berkurang) → affected = 0 → return 422.
Kenapa atomic SQL, bukan optimistic locking?
Lebih sederhana — tidak perlu retry loop.
Row-level lock di PostgreSQL sudah menjamin atomicity.
Untuk operasi increment/decrement, ini pola paling efisien.
Kenapa bukan pessimistic locking (SELECT ... FOR UPDATE)?
Butuh dua round-trip ke database (SELECT dulu, baru UPDATE).
Atomic UPDATE lebih cepat karena hanya satu statement.

Skenario B — Concurrent Status Update
Masalah: Dua admin update status order yang sama (satu Shipped, satu Cancelled). Hanya satu yang boleh menang.

Solusi: Conditional UPDATE dengan expected current status.
UPDATE "Orders"
SET "Status" = @newStatus, "UpdatedAt" = now()
WHERE "Id" = @id AND "Status" IN (@validSources)

Cara kerja:
Setiap transisi punya daftar "status sumber yang valid". Misal Shipped hanya valid dari Confirmed.
UPDATE hanya match kalau status saat ini ada di daftar itu.
Kalau dua request bersamaan:
Request 1 (Shipped dari Confirmed): match → affected = 1 → sukses.
Request 2 (Cancelled): status sudah Shipped → tidak match → affected = 0 → 409.
Kenapa bukan optimistic concurrency token (RowVersion)?
Conditional WHERE pada Status sudah cukup sebagai "versi" alami.
Tidak perlu kolom tambahan, tidak perlu retry.

Skenario C — Idempotent Create Under Race
Masalah: Dua request POST dengan idempotency key sama tiba bersamaan sebelum salah satu commit.

Solusi: Unique constraint pada tabel IdempotencyRecords.

Cara kerja:
Saat POST dengan header Idempotency-Key, server coba INSERT ke IdempotencyRecords.
Karena Key adalah PRIMARY KEY, hanya satu INSERT yang berhasil.
Request kedua mendapat unique violation → baca record lama:
Hash body sama → replay response lama.
Hash body beda → 409 Conflict.
Hasilnya: hanya satu order dibuat.
Kenapa unique constraint, bukan distributed lock (Redis)?
Database constraint adalah single source of truth.
Tidak butuh infrastruktur tambahan.
Tidak bisa di-bypass oleh race condition.
Lebih sederhana, lebih murah.

🆔 Strategi Idempotency
Klien Bertanggung Jawab Generate Key
Idempotency-Key di-generate oleh client (frontend/mobile app) sekali saat user memicu aksi, lalu dipakai ulang saat retry.

async function submitOrder(orderData) {
  const idempotencyKey = crypto.randomUUID();  // generate sekali
  return fetch('/api/orders', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': idempotencyKey,
    },
    body: JSON.stringify(orderData),
  });
}

Kalau request gagal (timeout), frontend retry dengan key yang sama → server replay response lama, tidak membuat order baru.

Perilaku Server
Kondisi	Response
POST tanpa Idempotency-Key	Diproses normal (lihat catatan testing di bawah)
POST dengan key baru	Diproses, response disimpan
POST dengan key sama + body sama	Replay response lama
POST dengan key sama + body beda	409 Conflict
GET/PATCH/DELETE	Idempotency tidak di-enforce

⚠️ Catatan Testing — Idempotency-Key Boleh Kosong
Untuk memudahkan testing manual di Swagger, saat ini aplikasi mengizinkan POST tanpa header Idempotency-Key. Request akan diproses normal tanpa idempotency check.
Alasan: mempercepat testing dan demo, terutama saat reviewer ingin mencoba endpoint langsung dari Swagger tanpa perlu menambah header manual.

Untuk production, disarankan:
Enforce header Idempotency-Key wajib ada untuk POST /api/orders.

Alternatif: pasang action filter [RequireIdempotencyKey] hanya di endpoint sensitif.
Cara mengaktifkan enforce di masa depan:

// Di IdempotencyMiddleware, ubah blok pengecekan header menjadi:
if (!context.Request.Headers.TryGetValue(HeaderName, out var keyValues) ||
    string.IsNullOrWhiteSpace(keyValues.FirstOrDefault()))
{
    context.Response.StatusCode = StatusCodes.Status400BadRequest;
    await context.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = 400,
        Title = "Missing Idempotency-Key header",
        Detail = "POST requests must include an 'Idempotency-Key' header."
    });
    return;
}

Cara Test Idempotency di Swagger
Buka POST /api/orders di Swagger.
Klik Try it out.
Tambahkan header:
Name: Idempotency-Key
Value: test-key-123 (atau UUID apa pun)
Submit dengan body tertentu → 201 Created.
Submit lagi dengan key sama dan body sama → response di-replay, tidak ada order baru.
Submit dengan key sama tapi body beda → 409 Conflict.

Verifikasi di database:
docker compose exec db psql -U postgres -d orderdb -c 'SELECT COUNT(*) FROM "Orders";'

🛡️ Race Condition Lain yang Dicegah
Selain 3 skenario di atas, ada 2 race condition lain yang diidentifikasi dan dicegah:

1. Double Cancel → Stock Dikembalikan Dua Kali
Skenario: Dua request cancel untuk order yang sama datang bersamaan.
Bahaya: Kalau keduanya lolos, stock akan dikembalikan dua kali → stock lebih besar dari seharusnya → data inconsistency.
Pencegahan: Conditional update pada status order sebelum restore stock:

UPDATE "Orders"
SET "Status" = 'Cancelled', "UpdatedAt" = now()
WHERE "Id" = @id AND "Status" IN ('Pending', 'Confirmed')

Hanya satu request yang berhasil update status (affected = 1). Yang kalah (affected = 0) langsung return 409 tanpa menjalankan restore stock.

Lokasi kode: OrderService.CancelOrderAsync

2. Duplicate Product dalam Satu Order
Skenario: Client mengirim order dengan dua item productId yang sama:

{
  "items": [
    { "productId": "A", "quantity": 5 },
    { "productId": "A", "quantity": 10 }
  ]
}

Bahaya: Kalau deduction dijalankan per-item, Product A di-deduct 5, lalu 10 secara terpisah. Kalau stok cuma 12, deduction pertama lolos (sisa 7), deduction kedua gagal → seluruh order di-rollback. Tidak salah secara keamanan, tapi tidak efisien dan bisa memberi hasil yang berbeda dibanding kalau diagregasi dulu.

Pencegahan: Agregasi quantity per ProductId sebelum deduction:

var grouped = order.Items
    .GroupBy(i => i.ProductId)
    .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
    .ToList();

Sekarang Product A di-deduct 15 sekaligus. Kalau stok tidak cukup, langsung gagal di awal.

Lokasi kode: OrderService.CreateOrderAsync

3. Idempotency Replay Race
Skenario: Dua request dengan key sama datang bersamaan, keduanya belum commit.

Bahaya: Kalau tidak dijaga, dua order bisa terbuat.

Pencegahan: Sudah dibahas di Skenario C — unique constraint pada IdempotencyRecords.Key.

📝 Logging & Tracing
Correlation ID
Setiap request punya correlation ID yang bisa ditrace antar log:

Dari client: kirim header X-Correlation-ID: <id> → server pakai ID itu.

Tanpa header: server generate UUID baru.

Response: server kembalikan ID yang sama di header X-Correlation-ID.

Log: setiap log yang di-emit selama request otomatis menyertakan ID ini.

Format Log
[14:23:45 INF] [abc-123] Order created: 4ec20233-...
[14:23:45 INF] [abc-123] Stock deducted for product a1b2c3...
[14:23:46 WRN] [def-456] Insufficient stock for product xyz...
[14:23:47 ERR] [def-456] Unhandled exception for POST /api/orders

Cara Melihat Log
# 50 baris terakhir
docker compose logs api --tail 50

# Live
docker compose logs api -f

# Filter error
docker compose logs api | grep "ERR"

# Trace request tertentu
docker compose logs api | grep "abc-123"

Level Log
Level	    Kapan
Information	Kejadian normal (order created, request start)
Warning	    Sesuatu tidak normal tapi bukan error (insufficient stock)
Error	    Exception yang tertangkap middleware
Fatal	    Aplikasi crash

⚠️ Error Handling
Semua endpoint mengembalikan format error yang konsisten (RFC 7807 ProblemDetails).

Format
{
  "type": "https://example.com/errors/insufficient-stock",
  "title": "Insufficient stock",
  "status": 422,
  "detail": "Stock for product <uuid> is not enough",
  "instance": "/api/orders"
}

Status Code yang Dipakai
Code	Kapan
200	Sukses (GET, PATCH, cancel)
201	Order berhasil dibuat
400	Validasi gagal (field kosong, format salah)
404	Order tidak ditemukan
409	Conflict (transisi invalid, cancel tidak boleh, idempotency reuse)
422	Stock tidak cukup
500	Unhandled exception

🧪 Testing
Prasyarat
Docker Desktop running (Testcontainers butuh Docker daemon).

.NET 10 SDK.

Menjalankan Semua Test
dotnet test tests/OrderManagement.Tests/OrderManagement.Tests.csproj

Menjalankan Test Spesifik
# Unit test transisi status
dotnet test tests/OrderManagement.Tests/OrderManagement.Tests.csproj \
  --filter "FullyQualifiedName~OrderStatusTransitionTests"

# Concurrency test
dotnet test tests/OrderManagement.Tests/OrderManagement.Tests.csproj \
  --filter "FullyQualifiedName~OrderConcurrencyTests"

Daftar Test
Unit Test (OrderStatusTransitionTests)
Validasi semua transisi status yang diizinkan.

Validasi semua transisi status yang ditolak.

Validasi terminal state (Delivered, Cancelled).

Integration Test (OrderConcurrencyTests)
Test	                                                            Skenario
ConcurrentStockDeduction_ShouldNotExceedStock	                    Skenario A — stok 15, dua order qty 10 bersamaan → satu sukses, satu 422, stok akhir 5
ConcurrentStatusUpdate_OnlyOneShouldWin	                            Skenario B — dua admin update bersamaan → satu sukses, satu 409
ConcurrentCreateWithSameIdempotencyKey_ShouldCreateOnlyOneOrder	    Skenario C — dua POST dengan key sama → hanya satu order terbuat

Bagaimana Integration Test Bekerja
1. CustomWebApplicationFactory start PostgreSQL container via Testcontainers.
2. WebApplicationFactory start API in-memory yang connect ke container itu.
3. Test mengirim request paralel via HttpClient.
4. Setelah test selesai, container dibersihkan otomatis.

🗄️ Skema Database
Products
Kolom	                Tipe	            Keterangan
Id	                    uuid	            PK
Name	                varchar(200)	    Nama produk
StockQuantity	        integer	            Stok, tidak boleh minus
Price	                numeric(18,2)	    Harga

Orders
Kolom	                Tipe	            Keterangan
Id	                    uuid	            PK
CustomerId	            uuid	            ID customer
ShippingAddress	        varchar(500)	    Alamat kirim
Status	                varchar(20)	        Pending/Confirmed/Shipped/Delivered/Cancelled
CreatedAt	            timestamptz	
UpdatedAt	            timestamptz	        nullable

OrderItems
Kolom	                Tipe	            Keterangan
Id	                    uuid	            PK
OrderId	                uuid	            FK → Orders (CASCADE)
ProductId	            uuid	            FK → Products
Quantity	            integer	
UnitPrice	            numeric(18,2)	

IdempotencyRecords
Kolom	                Tipe	            Keterangan
Key	                    varchar(128)	    PK — unique constraint di sini
RequestHash	            varchar(64)	        SHA256 dari body request
ResponseBody	        text	            Response yang di-cache
StatusCode	            integer	            HTTP status yang di-cache
CreatedAt	            timestamptz

Schema File
Schema didefinisikan di schema.sql (di root solution) dan dieksekusi saat startup via DatabaseExtensions.ApplySchemaAsync(). File ini juga bisa dijalankan manual di psql:
docker compose exec db psql -U postgres -d orderdb -f /path/to/schema.sql

🎯 Keputusan Desain
Kenapa PostgreSQL, bukan SQLite?
SQLite: locking bersifat database-level — hanya satu writer dalam satu waktu. Tidak bisa test concurrency sesungguhnya.

PostgreSQL: mendukung row-level locking dan MVCC — concurrent transaction bisa jalan paralel pada baris berbeda, dan row yang sama di-serialize dengan benar.

SQL Server: juga bisa, tapi image Docker-nya besar (~1.5GB vs ~80MB untuk Postgres alpine).

Kenapa Atomic SQL UPDATE, bukan ORM biasa?
// ❌ Tidak aman untuk concurrent
var product = await _db.Products.FindAsync(id);
if (product.StockQuantity >= qty) {
    product.StockQuantity -= qty;
    await _db.SaveChangesAsync();
}

Di atas ada read-then-write gap — dua request bisa baca stok yang sama, lalu keduanya update. Hasilnya stok bisa minus.
// ✅ Aman
UPDATE "Products" SET "StockQuantity" = "StockQuantity" - @qty
WHERE "Id" = @id AND "StockQuantity" >= @qty

Satu statement, atomic, tidak ada gap.

Kenapa await using untuk transaction?
await using var transaction = await _db.Database.BeginTransactionAsync(ct);
// ... 
await transaction.CommitAsync(ct);

await using otomatis rollback saat exception. Tidak perlu rollback manual di catch — kalau dipanggil dua kali, akan error This NpgsqlTransaction has completed.

Kenapa Idempotency pakai tabel, bukan in-memory cache?
In-memory: hilang saat restart → tidak reliable.

Tabel + unique constraint: persist, aman dari race condition, single source of truth.

Redis: menambah infrastruktur yang tidak perlu untuk prototype.

Kenapa FluentValidation + SharpGrip, bukan FluentValidation.AspNetCore?
FluentValidation.AspNetCore sudah deprecated sejak 2024. Penggantinya adalah:

FluentValidation (core)

FluentValidation.DependencyInjectionExtensions (untuk registrasi DI)

SharpGrip.FluentValidation.AutoValidation.Mvc (untuk auto-validation MVC)

📌 Catatan Testing
Idempotency-Key Opsional
Untuk memudahkan testing manual di Swagger, header Idempotency-Key tidak diwajibkan saat ini. Request POST tanpa header akan diproses normal tanpa idempotency check.

Ini adalah kompromi untuk kenyamanan testing, bukan best practice production. Lihat bagian Strategi Idempotency untuk cara mengaktifkan enforce.

Seed Data
Produk contoh otomatis di-seed saat startup jika tabel Products kosong:

Name	Stock	Price
Product A	100	50.000
Product B	50	75.000
Product C	15	100.000
Product C dengan stok 15 cocok untuk test Skenario A (dua order qty 10, hanya satu yang boleh sukses).

Reset Database
docker compose down -v
docker compose up -d --build

📁 Struktur Proyek
OrderManagement/
├── OrderManagement.sln
├── Dockerfile
├── docker-compose.yml
├── schema.sql
├── README.md
├── src/
│   ├── OrderManagement.Domain/
│   │   ├── Entities/
│   │   │   ├── Product.cs
│   │   │   ├── Order.cs
│   │   │   ├── OrderItem.cs
│   │   │   └── IdempotencyRecord.cs
│   │   ├── Enums/
│   │   │   └── OrderStatus.cs
│   │   └── OrderStatusTransitions.cs
│   ├── OrderManagement.Infrastructure/
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs
│   │   │   └── Configurations/
│   │   └── Repositories/
│   └── OrderManagement.API/
│       ├── Program.cs
│       ├── Controllers/
│       ├── Services/
│       ├── Middleware/
│       ├── DTOs/
│       ├── Validators/
│       └── Extensions/
└── tests/
    └── OrderManagement.Tests/
        ├── Integration/
        │   ├── CustomWebApplicationFactory.cs
        │   └── OrderConcurrencyTests.cs
        └── Unit/
            └── OrderStatusTransitionTests.cs

📞 Kontak
Dibuat untuk test teknis ASTEK (Adaya Solusi Teknologi).

Untuk pertanyaan, hubungi: sidik46.ipb@gmail.com
