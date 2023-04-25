
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Polly;
using Microsoft.EntityFrameworkCore;
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
        private readonly IOptionsMonitor<AppSettings> _appSettings;
        private readonly AsyncPolicy _retryPolicy;
        private readonly FuelPricingContext _dbContext;

        public FuelPricingService(IHttpClientFactory httpClientFactory,
            IOptionsMonitor<AppSettings> appSettings, ILogger<FuelPricingService> logger, FuelPricingContext dbContext)
        {
            _httpClientFactory = httpClientFactory;
            _appSettings = appSettings;
            _retryPolicy = Policy.Handle<HttpRequestException>()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
            _logger = logger;
            _dbContext = dbContext;
        }

        public async Task StartAsync(CancellationToken stoppingToken)
        {
            using var httpClient = _httpClientFactory.CreateClient("FuelPricingClient");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var response = await _retryPolicy.ExecuteAsync(() => httpClient.GetAsync(_appSettings.CurrentValue.Uri, stoppingToken));
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync(stoppingToken);
                    var jsonData = JsonConvert.DeserializeObject<FuelPriceJson>(content);
                    var firstSeries = jsonData?.Response?.Data?.FirstOrDefault();
                    var cutoffDate = DateTimeOffset.UtcNow.AddDays(-_appSettings.CurrentValue.FuelDaysCount);

                    if (firstSeries != null)
                    {
                        await AddNewFuelPriceIfNotExistAsync(firstSeries, cutoffDate);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while processing fuel pricing data.");
                }

                // Wait for either the delay or the token to be cancelled
                var completedTask = await Task.WhenAny(
                    Task.Delay(TimeSpan.FromMinutes(_appSettings.CurrentValue.DelayBetweenExecutions), stoppingToken),
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
        }

        private async Task AddNewFuelPriceIfNotExistAsync(Data firstSeries, DateTimeOffset cutoffDate)
        {
            var period = firstSeries?.Period;
            if (DateTimeOffset.TryParse(period, out var parsedDate) && parsedDate >= cutoffDate)
            {
                var existingRecord = await _dbContext.FuelPrices!.FirstOrDefaultAsync(fp => fp.Period == period);
                if (existingRecord == null)
                {
                    var value = firstSeries?.Value ?? 0m;
                    await _dbContext.FuelPrices!.AddAsync(new FuelPrices { Period = period, Value = value });
                    await _dbContext.SaveChangesAsync();
                }
            }
        }
    }
}