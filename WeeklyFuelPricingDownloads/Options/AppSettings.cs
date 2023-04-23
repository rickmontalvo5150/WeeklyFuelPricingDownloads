
namespace WeeklyFuelPricingDownloads.Options
{
    public class AppSettings
    {
        public string FuelPricingConnection { get; set; } = string.Empty;
        public int FuelDaysCount { get; set; }
        public int DelayBetweenExecutions { get; set; }
        public Uri? Uri { get; set; }
    }
}