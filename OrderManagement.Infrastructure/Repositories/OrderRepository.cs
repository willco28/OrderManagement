using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Domain.Interfaces;
using OrderManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrderManagement.Infrastructure.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly AppDbContext _db;

        public OrderRepository(AppDbContext db) => _db = db;

        public Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => _db.Orders
                  .Include(o => o.Items)
                  .ThenInclude(i => i.Product)
                  .FirstOrDefaultAsync(o => o.Id == id, ct);

        public async Task<IReadOnlyList<Order>> ListAsync(
            OrderStatus? status, Guid? customerId,
            DateTime? from, DateTime? to,
            int page, int pageSize, CancellationToken ct = default)
        {
            var query = BuildQuery(status, customerId, from, to);
            return await query
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);
        }

        public Task<int> CountAsync(
            OrderStatus? status, Guid? customerId,
            DateTime? from, DateTime? to, CancellationToken ct = default)
            => BuildQuery(status, customerId, from, to).CountAsync(ct);

        public async Task AddAsync(Order order, CancellationToken ct = default)
        {
            _db.Orders.Add(order);
            await _db.SaveChangesAsync(ct);
        }

        private IQueryable<Order> BuildQuery(
            OrderStatus? status, Guid? customerId, DateTime? from, DateTime? to)
        {
            var query = _db.Orders.Include(o => o.Items).AsQueryable();
            if (status.HasValue) query = query.Where(o => o.Status == status);
            if (customerId.HasValue) query = query.Where(o => o.CustomerId == customerId);
            if (from.HasValue) query = query.Where(o => o.CreatedAt >= from);
            if (to.HasValue) query = query.Where(o => o.CreatedAt <= to);
            return query;
        }
    }
}
