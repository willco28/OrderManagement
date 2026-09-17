using OrderManagement.Domain;
using OrderManagement.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using FluentAssertions;

namespace OrderManagement.Test.Unit
{
    public class OrderStatusTransitionTests
    {
        [Theory]
        [InlineData(OrderStatus.Pending, OrderStatus.Confirmed)]
        [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
        [InlineData(OrderStatus.Confirmed, OrderStatus.Shipped)]
        [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
        [InlineData(OrderStatus.Shipped, OrderStatus.Delivered)]
        public void IsValid_WithAllowedTransition_ReturnsTrue(
            OrderStatus from, OrderStatus to)
        {
            OrderStatusTransitions.IsValid(from, to).Should().BeTrue();
        }

        [Theory]
        [InlineData(OrderStatus.Pending, OrderStatus.Shipped)]
        [InlineData(OrderStatus.Pending, OrderStatus.Delivered)]
        [InlineData(OrderStatus.Confirmed, OrderStatus.Delivered)]
        [InlineData(OrderStatus.Shipped, OrderStatus.Cancelled)]
        [InlineData(OrderStatus.Delivered, OrderStatus.Pending)]
        [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
        public void IsValid_WithDisallowedTransition_ReturnsFalse(
            OrderStatus from, OrderStatus to)
        {
            OrderStatusTransitions.IsValid(from, to).Should().BeFalse();
        }

        [Theory]
        [InlineData(OrderStatus.Delivered)]
        [InlineData(OrderStatus.Cancelled)]
        public void IsTerminal_ForDeliveredAndCancelled_ReturnsTrue(OrderStatus status)
        {
            OrderStatusTransitions.IsTerminal(status).Should().BeTrue();
        }

        [Theory]
        [InlineData(OrderStatus.Pending)]
        [InlineData(OrderStatus.Confirmed)]
        [InlineData(OrderStatus.Shipped)]
        public void IsTerminal_ForNonTerminalStates_ReturnsFalse(OrderStatus status)
        {
            OrderStatusTransitions.IsTerminal(status).Should().BeFalse();
        }

        [Fact]
        public void ValidTargets_FromPending_ContainsConfirmedAndCancelled()
        {
            var targets = OrderStatusTransitions.ValidTargets(OrderStatus.Pending);
            targets.Should().BeEquivalentTo(new[] { OrderStatus.Confirmed, OrderStatus.Cancelled });
        }
    }
}
