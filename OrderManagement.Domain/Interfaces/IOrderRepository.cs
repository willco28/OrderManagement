using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrderManagement.Domain.Interfaces
{
    public interface IOrderRepository
    {
        Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IReadOnlyList<Order>> ListAsync(
            OrderStatus? status,
            Guid? customerId,
            DateTime? from,
            DateTime? to,
            int page,
            int pageSize,
            CancellationToken ct = default);
        Task<int> CountAsync(
            OrderStatus? status,
            Guid? customerId,
            DateTime? from,
            DateTime? to,
            CancellationToken ct = default);
        Task AddAsync(Order order, CancellationToken ct = default);
    }
}
