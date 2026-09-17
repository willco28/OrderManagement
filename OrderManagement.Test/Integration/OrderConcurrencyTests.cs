using FluentAssertions;
using OrderManagement.API.DTOs;
using OrderManagement.Domain.Entities;
using OrderManagement.Infrastructure.Data;
using System.Net;
using System.Net.Http.Json;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OrderManagement.Test.Integration
{
    public class OrderConcurrencyTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;

        public OrderConcurrencyTests(CustomWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private async Task<Guid> SeedProductAsync(int stock)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = "Test Product",
                StockQuantity = stock,
                Price = 100m
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            return product.Id;
        }

        [Fact]
        public async Task ConcurrentStockDeduction_ShouldNotExceedStock()
        {
            // Arrange: stock = 15, two orders of qty 10
            var productId = await SeedProductAsync(15);

            // Act: fire both requests simultaneously
            var client1 = _factory.CreateClient();
            var client2 = _factory.CreateClient();

            var request = new CreateOrderRequest
            {
                CustomerId = Guid.NewGuid(),
                ShippingAddress = "Test Address",
                Items = new List<OrderItemRequest>
            {
                new() { ProductId = productId, Quantity = 10 }
            }
            };

            var task1 = client1.PostAsJsonAsync("/api/orders", request);
            var task2 = client2.PostAsJsonAsync("/api/orders", request);

            var responses = await Task.WhenAll(task1, task2);

            // Assert: exactly one succeeds, one fails with 422
            var successCount = responses.Count(r => r.IsSuccessStatusCode);
            var failCount = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);

            successCount.Should().Be(1, "only one order should succeed");
            failCount.Should().Be(1, "the other should fail with insufficient stock");

            // Verify final stock
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var product = await db.Products.FindAsync(productId);
            product!.StockQuantity.Should().Be(5, "stock should be 15 - 10 = 5");
        }

        [Fact]
        public async Task ConcurrentStatusUpdate_OnlyOneShouldWin()
        {
            // Arrange: create a confirmed order
            var productId = await SeedProductAsync(100);
            var client = _factory.CreateClient();

            var createResponse = await client.PostAsJsonAsync("/api/orders", new CreateOrderRequest
            {
                CustomerId = Guid.NewGuid(),
                ShippingAddress = "Test",
                Items = new List<OrderItemRequest> { new() { ProductId = productId, Quantity = 1 } }
            });

            var created = await createResponse.Content.ReadFromJsonAsync<OrderResponse>();
            var orderId = created!.Id;

            // First confirm the order
            await client.PatchAsJsonAsync($"/api/orders/{orderId}/status",
                new UpdateStatusRequest { Status = Domain.Enums.OrderStatus.Confirmed });

            // Act: two admins try to update simultaneously — one Shipped, one Cancelled
            var admin1 = _factory.CreateClient();
            var admin2 = _factory.CreateClient();

            var task1 = admin1.PatchAsJsonAsync($"/api/orders/{orderId}/status",
                new UpdateStatusRequest { Status = Domain.Enums.OrderStatus.Shipped });
            var task2 = admin2.PostAsync($"/api/orders/{orderId}/cancel", null);

            var responses = await Task.WhenAll(task1, task2);

            // Assert: exactly one succeeds
            var successCount = responses.Count(r => r.IsSuccessStatusCode);
            successCount.Should().Be(1, "only one admin should win");
        }

        [Fact]
        public async Task ConcurrentCreateWithSameIdempotencyKey_ShouldCreateOnlyOneOrder()
        {
            // Arrange
            var productId = await SeedProductAsync(100);
            var idempotencyKey = Guid.NewGuid().ToString();
            var request = new CreateOrderRequest
            {
                CustomerId = Guid.NewGuid(),
                ShippingAddress = "Test",
                Items = new List<OrderItemRequest> { new() { ProductId = productId, Quantity = 1 } }
            };

            // Act: two identical requests with same idempotency key, simultaneously
            var client1 = _factory.CreateClient();
            var client2 = _factory.CreateClient();

            client1.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
            client2.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);

            var task1 = client1.PostAsJsonAsync("/api/orders", request);
            var task2 = client2.PostAsJsonAsync("/api/orders", request);

            await Task.WhenAll(task1, task2);

            // Assert: only one order exists in DB
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orderCount = await db.Orders.CountAsync();
            orderCount.Should().Be(1, "idempotency key should prevent duplicate orders");
        }
    }
}
