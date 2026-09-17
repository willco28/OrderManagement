using Microsoft.EntityFrameworkCore;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Infrastructure.Data;
using System.Net.NetworkInformation;

namespace OrderManagement.API.Services
{
    public class OrderService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<OrderService> _logger;

        public OrderService(AppDbContext db, ILogger<OrderService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<Order> CreateOrderAsync(Order order, CancellationToken ct)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                // Aggregate quantities per product to prevent duplicates
                var grouped = order.Items
                    .GroupBy(i => i.ProductId)
                    .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
                    .ToList();

                foreach (var item in grouped)
                {
                    // ATOMIC stock deduction — single SQL UPDATE with conditional WHERE
                    var affected = await _db.Database.ExecuteSqlRawAsync(
                        @"UPDATE ""Products""
                      SET ""StockQuantity"" = ""StockQuantity"" - {0}
                      WHERE ""Id"" = {1} AND ""StockQuantity"" >= {0}",
                        [item.Quantity, item.ProductId], ct);

                    if (affected == 0)
                    {
                        await transaction.RollbackAsync(ct);
                        throw new InvalidOperationException(
                            $"Insufficient stock for product {item.ProductId}");
                    }
                }

                _db.Orders.Add(order);
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return order;
            }
            catch
            {
                //await transaction.RollbackAsync(ct);
                throw;
            }
        }

        public async Task<bool> UpdateStatusAsync(Guid orderId, OrderStatus newStatus, CancellationToken ct)
        {
            // Conditional UPDATE — only succeeds if current status matches valid source
            var validSources = newStatus switch
            {
                OrderStatus.Confirmed => new[] { OrderStatus.Pending },
                OrderStatus.Shipped => new[] { OrderStatus.Confirmed },
                OrderStatus.Delivered => new[] { OrderStatus.Shipped },
                OrderStatus.Cancelled => new[] { OrderStatus.Pending, OrderStatus.Confirmed },
                _ => Array.Empty<OrderStatus>()
            };

            if (validSources.Length == 0)
                throw new InvalidOperationException("Invalid status transition");
            var status = newStatus.ToString();
            // Build SQL with IN clause for valid sources
            var sourceList = string.Join(",", validSources.Select(s => $"'{s}'"));
            var sql = @"UPDATE ""Orders"" SET ""Status"" = {0}, ""UpdatedAt"" = now()
                WHERE ""Id"" = {1} AND ""Status"" IN (" + sourceList + ")";

            var affected = await _db.Database.ExecuteSqlRawAsync(
                sql,
                [status, orderId],
                ct);

            return affected > 0;
        }

        public async Task<bool> CancelOrderAsync(Guid orderId, CancellationToken ct)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);

            try
            {
                var order = await _db.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == orderId, ct);

                if (order is null) return false;

                // Atomic conditional status update
                var affected = await _db.Database.ExecuteSqlRawAsync(
                    @"UPDATE ""Orders"" SET ""Status"" = 'Cancelled', ""UpdatedAt"" = now()
                  WHERE ""Id"" = {0} AND ""Status"" IN ('Pending', 'Confirmed')",
                    orderId);

                if (affected == 0)
                {
                    await transaction.RollbackAsync(ct);
                    return false;
                }

                // Restore stock
                foreach (var item in order.Items)
                {
                    await _db.Database.ExecuteSqlRawAsync(
                        @"UPDATE ""Products"" SET ""StockQuantity"" = ""StockQuantity"" + {0}
                      WHERE ""Id"" = {1}",
                        [item.Quantity, item.ProductId], ct);
                }

                await transaction.CommitAsync(ct);
                return true;
            }
            catch
            {
                //await transaction.RollbackAsync(ct);
                throw;
            }
        }
    }
}
