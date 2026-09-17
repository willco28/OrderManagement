-- Products
CREATE TABLE IF NOT EXISTS "Products" (
    "Id" uuid PRIMARY KEY,
    "Name" varchar(200) NOT NULL,
    "StockQuantity" integer NOT NULL,
    "Price" numeric(18,2) NOT NULL
);

-- Orders
CREATE TABLE IF NOT EXISTS "Orders" (
    "Id" uuid PRIMARY KEY,
    "CustomerId" uuid NOT NULL,
    "ShippingAddress" varchar(500) NOT NULL,
    "Status" varchar(20) NOT NULL,
    "CreatedAt" timestamptz NOT NULL,
    "UpdatedAt" timestamptz NULL
);

-- OrderItems
CREATE TABLE IF NOT EXISTS "OrderItems" (
    "Id" uuid PRIMARY KEY,
    "OrderId" uuid NOT NULL,
    "ProductId" uuid NOT NULL,
    "Quantity" integer NOT NULL,
    "UnitPrice" numeric(18,2) NOT NULL,
    CONSTRAINT "FK_OrderItems_Orders_OrderId"
        FOREIGN KEY ("OrderId") REFERENCES "Orders"("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_OrderItems_Products_ProductId"
        FOREIGN KEY ("ProductId") REFERENCES "Products"("Id")
);

CREATE INDEX IF NOT EXISTS "IX_OrderItems_OrderId" ON "OrderItems"("OrderId");
CREATE INDEX IF NOT EXISTS "IX_OrderItems_ProductId" ON "OrderItems"("ProductId");

-- IdempotencyRecords
CREATE TABLE IF NOT EXISTS "IdempotencyRecords" (
    "Key" varchar(128) PRIMARY KEY,
    "RequestHash" varchar(64) NOT NULL,
    "ResponseBody" text NULL,
    "StatusCode" integer NOT NULL,
    "CreatedAt" timestamptz NOT NULL
);