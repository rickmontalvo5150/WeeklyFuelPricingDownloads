
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WeeklyFuelPricingDownloads.DataContext;
using WeeklyFuelPricingDownloads.Options;
using WeeklyFuelPricingDownloads.Services;

namespace WeeklyFuelPricingDownloads
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appSettings.json", optional: true, reloadOnChange: true);

            IConfiguration configuration = builder.Build();

            var host = new HostBuilder()
                .ConfigureServices((hostContext, services) => 
                {
                    var appSettings = new AppSettings();
                    services.Configure<AppSettings>(configuration.GetSection(nameof(appSettings)));
                    configuration.GetSection(nameof(AppSettings)).Bind(appSettings);

                    services.AddHttpClient();
                    services.AddDbContext<FuelPricingContext>(options => options.UseSqlServer(appSettings.FuelPricingConnection));
                    services.AddHostedService<FuelPricingService>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}