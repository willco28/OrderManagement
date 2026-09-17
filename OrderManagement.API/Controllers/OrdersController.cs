using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderManagement.API.DTOs;
using OrderManagement.API.Services;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Infrastructure.Data;

namespace OrderManagement.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly OrderService _orderService;
        private readonly AppDbContext _db;

        public OrdersController(OrderService orderService, AppDbContext db)
        {
            _orderService = orderService;
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrder(
            [FromBody] CreateOrderRequest request, CancellationToken ct)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                CustomerId = request.CustomerId,
                ShippingAddress = request.ShippingAddress,
                Items = request.Items.Select(i => new OrderItem
                {
                    Id = Guid.NewGuid(),
                    ProductId = i.ProductId,
                    Quantity = i.Quantity
                }).ToList()
            };

            try
            {
                var created = await _orderService.CreateOrderAsync(order, ct);
                return CreatedAtAction(nameof(GetOrder), new { id = created.Id },
                    OrderResponse.From(created));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Insufficient stock"))
            {
                return UnprocessableEntity(new ProblemDetails
                {
                    Status = 422,
                    Title = "Insufficient stock",
                    Detail = ex.Message
                });
            }
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetOrder(Guid id, CancellationToken ct)
        {
            var order = await _db.Orders
                .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == id, ct);

            return order is null ? NotFound() : Ok(OrderResponse.From(order));
        }

        [HttpGet]
        public async Task<IActionResult> ListOrders(
            [FromQuery] OrderStatus? status,
            [FromQuery] Guid? customerId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            var query = _db.Orders
                .Include(o => o.Items)
                .AsNoTracking()
                .AsQueryable();

            if (status.HasValue) query = query.Where(o => o.Status == status);
            if (customerId.HasValue) query = query.Where(o => o.CustomerId == customerId);
            if (from.HasValue) query = query.Where(o => o.CreatedAt >= from);
            if (to.HasValue) query = query.Where(o => o.CreatedAt <= to);

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(o => OrderResponse.From(o))
                .ToListAsync(ct);

            return Ok(new PagedResult<OrderResponse>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            });
        }

        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> UpdateStatus(
            Guid id, [FromBody] UpdateStatusRequest request, CancellationToken ct)
        {
            var success = await _orderService.UpdateStatusAsync(id, request.Status, ct);

            return success
                ? Ok(new { message = "Status updated" })
                : Conflict(new ProblemDetails
                {
                    Status = 409,
                    Title = "Invalid status transition",
                    Detail = "The order is in a state that does not allow this transition."
                });
        }

        [HttpPost("{id:guid}/cancel")]
        public async Task<IActionResult> CancelOrder(Guid id, CancellationToken ct)
        {
            var success = await _orderService.CancelOrderAsync(id, ct);

            return success
                ? Ok(new { message = "Order cancelled" })
                : Conflict(new ProblemDetails
                {
                    Status = 409,
                    Title = "Cannot cancel order",
                    Detail = "Only Pending or Confirmed orders can be cancelled."
                });
        }
    }
}
