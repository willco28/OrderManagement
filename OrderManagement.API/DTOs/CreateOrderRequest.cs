using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace OrderManagement.API.DTOs
{
    public class CreateOrderRequest
    {
        [Required]
        public Guid CustomerId { get; set; }

        [Required, MinLength(1)]
        public List<OrderItemRequest> Items { get; set; } = new();

        [Required, MaxLength(500)]
        public string ShippingAddress { get; set; } = string.Empty;
    }

    public class OrderItemRequest
    {
        [Required]
        public Guid ProductId { get; set; }

        [Range(1, int.MaxValue)]
        public int Quantity { get; set; }
    }
}
