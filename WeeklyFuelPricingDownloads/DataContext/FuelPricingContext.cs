
using Microsoft.EntityFrameworkCore;
using WeeklyFuelPricingDownloads.Models;

namespace WeeklyFuelPricingDownloads.DataContext
{
    public class FuelPricingContext : DbContext
    {
        public FuelPricingContext(DbContextOptions<FuelPricingContext> options) : base(options)
        {
        }

        public DbSet<FuelPrices>? FuelPrices { get; set; }
    }
}