using OrderManagement.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace OrderManagement.API.DTOs
{
    public class UpdateStatusRequest
    {
        [Required]
        public OrderStatus Status { get; set; }
    }
}
