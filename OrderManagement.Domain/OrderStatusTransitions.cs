using OrderManagement.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrderManagement.Domain
{
    public static class OrderStatusTransitions
    {
        private static readonly Dictionary<OrderStatus, OrderStatus[]> Allowed = new()
        {
            [OrderStatus.Pending] = new[] { OrderStatus.Confirmed, OrderStatus.Cancelled },
            [OrderStatus.Confirmed] = new[] { OrderStatus.Shipped, OrderStatus.Cancelled },
            [OrderStatus.Shipped] = new[] { OrderStatus.Delivered },
            [OrderStatus.Delivered] = Array.Empty<OrderStatus>(),
            [OrderStatus.Cancelled] = Array.Empty<OrderStatus>()
        };

        public static bool IsValid(OrderStatus from, OrderStatus to)
            => Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

        public static bool IsTerminal(OrderStatus status)
            => Allowed.TryGetValue(status, out var targets) && targets.Length == 0;

        public static IReadOnlyList<OrderStatus> ValidTargets(OrderStatus from)
            => Allowed.TryGetValue(from, out var targets) ? targets : Array.Empty<OrderStatus>();
    }
}
