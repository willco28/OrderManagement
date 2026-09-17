using System;
using System.Collections.Generic;
using System.Text;

namespace OrderManagement.Domain.Entities
{
    public class IdempotencyRecord
    {
        public string Key { get; set; } = string.Empty;
        public string RequestHash { get; set; } = string.Empty;
        public string ResponseBody { get; set; } = string.Empty;
        public int StatusCode { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
