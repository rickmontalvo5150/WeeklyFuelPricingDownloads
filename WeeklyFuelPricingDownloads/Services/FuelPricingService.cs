
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Polly;
using WeeklyFuelPricingDownloads.DataContext;
using WeeklyFuelPricingDownloads.Models;
using WeeklyFuelPricingDownloads.Options;

namespace WeeklyFuelPricingDownloads.Services
{
    public class FuelPricingService : IHostedService, IDisposable
    {
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<FuelPricingService> _logger;
        private readonly IServiceProvider _services;
        private readonly AppSettings _appSettings;
        private readonly AsyncPolicy _retryPolicy;

        public FuelPricingService(IServiceProvider services, IHttpClientFactory httpClientFactory, IOptions<AppSettings> appSettings, ILogger<FuelPricingService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _appSettings = appSettings.Value;
            _services = services;
            _retryPolicy = Policy.Handle<HttpRequestException>()
                                 .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken stoppingToken)
        {
            using var httpClient = _httpClientFactory.CreateClient("FuelPricingClient");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<FuelPricingContext>();
                    var response = await _retryPolicy.ExecuteAsync(() => httpClient.GetAsync(_appSettings.Uri, stoppingToken));

                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync(stoppingToken);
                    var jsonData = JsonConvert.DeserializeObject<FuelPriceJson>(content);
                    var firstSeries = jsonData?.Response?.Data?.FirstOrDefault();
                    var cutoffDate = DateTimeOffset.UtcNow.AddDays(-_appSettings.FuelDaysCount);

                    if (firstSeries != null)
                    {
                        AddNewFuelPriceIfNotExist(dbContext, firstSeries, cutoffDate);
                        if (dbContext.ChangeTracker.HasChanges())
                        {
                            await dbContext.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while processing fuel pricing data.");
                }

                // Wait for either the delay or the token to be cancelled
                var completedTask = await Task.WhenAny(
                    Task.Delay(TimeSpan.FromMinutes(_appSettings.DelayBetweenExecutions), stoppingToken),
                    Task.Delay(Timeout.Infinite, stoppingToken));

                // If the stoppingToken was cancelled, exit the loop
                if (completedTask.IsCanceled)
                {
                    _logger.LogInformation("Delay task was cancelled. Exiting loop.");
                    break;
                }
            }
        }

        public Task StopAsync(CancellationToken stoppingToken)
        {
            _cancellationTokenSource.Cancel();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _cancellationTokenSource.Dispose();
            GC.SuppressFinalize(this);
        }

        private static void AddNewFuelPriceIfNotExist(FuelPricingContext dbContext, Data? firstSeries, DateTimeOffset cutoffDate)
        {
            var period = firstSeries?.Period;
            if (DateTimeOffset.TryParse(period, out var parsedDate) && parsedDate >= cutoffDate)
            {
                var existingRecord = dbContext.FuelPrices!.FirstOrDefault(fp => fp.Period == period);
                if (existingRecord == null)
                {
                    var value = firstSeries?.Value ?? 0m;
                    dbContext.FuelPrices!.Add(new FuelPrices {Period = period, Value = value});
                }
            }
        }
    }
}