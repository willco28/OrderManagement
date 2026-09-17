using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderManagement.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrderManagement.Infrastructure.Data.Configurations
{
    public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
    {
        public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
        {
            builder.HasKey(r => r.Key);
            builder.Property(r => r.Key).HasMaxLength(128);
            builder.Property(r => r.RequestHash).HasMaxLength(64).IsRequired();
            builder.Property(r => r.ResponseBody).HasColumnType("text");
            builder.Property(r => r.StatusCode).IsRequired();
        }
    }
}
