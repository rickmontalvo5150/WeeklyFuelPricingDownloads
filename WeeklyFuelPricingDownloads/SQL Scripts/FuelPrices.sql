
Use FuelPricing;

IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[FuelPrices]') AND type in (N'U'))
BEGIN
    CREATE TABLE FuelPrices (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Period VARCHAR(50) NOT NULL,
        Value DECIMAL(18,2) NOT NULL
    )
END