using Microsoft.EntityFrameworkCore;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;

namespace OrderManagement.API.Middleware
{
    public class IdempotencyMiddleware
    {
        private const string HeaderName = "Idempotency-Key";
        private readonly RequestDelegate _next;

        public IdempotencyMiddleware(RequestDelegate next) => _next = next;

        public async Task InvokeAsync(HttpContext context, AppDbContext db)
        {
            if (context.Request.Method != HttpMethods.Post ||
                !context.Request.Headers.TryGetValue(HeaderName, out var keyValues))
            {
                await _next(context);
                return;
            }

            var key = keyValues.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(key))
            {
                await _next(context);
                return;
            }

            // Read request body for hashing
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            var requestHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(body)));

            // Try to insert idempotency record atomically
            var record = new IdempotencyRecord
            {
                Key = key,
                RequestHash = requestHash,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                db.IdempotencyRecords.Add(record);
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Unique constraint violation -> key already exists
                var existing = await db.IdempotencyRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Key == key);

                if (existing is not null)
                {
                    if (existing.RequestHash != requestHash)
                    {
                        context.Response.StatusCode = StatusCodes.Status409Conflict;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = "Idempotency key reuse with different payload",
                            key
                        });
                        return;
                    }

                    // Replay cached response
                    context.Response.StatusCode = existing.StatusCode;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(existing.ResponseBody);
                    return;
                }
            }

            // First execution: buffer the response
            var originalBody = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;

            await _next(context);

            buffer.Position = 0;
            var responseBody = await new StreamReader(buffer).ReadToEndAsync();

            // Persist the response for replay
            record.ResponseBody = responseBody;
            record.StatusCode = context.Response.StatusCode;
            await db.SaveChangesAsync();

            buffer.Position = 0;
            await buffer.CopyToAsync(originalBody);
        }
    }
}
