using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FinSight.Models.ViewModels;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinSight.Services
{
    /// <summary>
    /// Service for fetching financial data from Alpha Vantage API.
    /// Includes in-memory caching to respect the free-tier rate limit (25 calls/day).
    /// </summary>
    public class AlphaVantageService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AlphaVantageService> _logger;

        private string ApiKey => _configuration["AlphaVantage:ApiKey"] ?? "";
        private string BaseUrl => _configuration["AlphaVantage:BaseUrl"] ?? "https://www.alphavantage.co/query";
        private int CacheMinutes => _configuration.GetValue("AlphaVantage:CacheMinutes", 60);

        public AlphaVantageService(
            HttpClient httpClient,
            IMemoryCache cache,
            IConfiguration configuration,
            ILogger<AlphaVantageService> logger)
        {
            _httpClient = httpClient;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════
        // EXCHANGE RATES
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// Fetches the real-time exchange rate for a single currency pair.
        /// </summary>
        public async Task<ExchangeRateItem?> GetExchangeRateAsync(string fromCurrency, string toCurrency)
        {
            var cacheKey = $"AV_ExRate_{fromCurrency}_{toCurrency}";

            if (_cache.TryGetValue(cacheKey, out ExchangeRateItem? cached))
            {
                return cached;
            }

            try
            {
                var url = $"{BaseUrl}?function=CURRENCY_EXCHANGE_RATE&from_currency={fromCurrency}&to_currency={toCurrency}&apikey={ApiKey}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);

                // Check for API error/rate limit messages
                if (json.RootElement.TryGetProperty("Note", out _) ||
                    json.RootElement.TryGetProperty("Information", out _))
                {
                    _logger.LogWarning("Alpha Vantage rate limit hit for {From}/{To}", fromCurrency, toCurrency);
                    return null;
                }

                if (!json.RootElement.TryGetProperty("Realtime Currency Exchange Rate", out var data))
                {
                    _logger.LogWarning("Unexpected response structure for {From}/{To}", fromCurrency, toCurrency);
                    return null;
                }

                var item = new ExchangeRateItem
                {
                    FromCurrency = GetJsonString(data, "1. From_Currency Code"),
                    FromCurrencyName = GetJsonString(data, "2. From_Currency Name"),
                    ToCurrency = GetJsonString(data, "3. To_Currency Code"),
                    ToCurrencyName = GetJsonString(data, "4. To_Currency Name"),
                    Rate = GetJsonDecimal(data, "5. Exchange Rate"),
                    BidPrice = GetJsonDecimal(data, "8. Bid Price"),
                    AskPrice = GetJsonDecimal(data, "9. Ask Price"),
                    LastRefreshed = GetJsonDateTime(data, "6. Last Refreshed"),
                    TimeZone = GetJsonString(data, "7. Time Zone")
                };

                _cache.Set(cacheKey, item, TimeSpan.FromMinutes(CacheMinutes));
                _logger.LogInformation("Fetched exchange rate: {From}/{To} = {Rate}", fromCurrency, toCurrency, item.Rate);
                return item;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch exchange rate for {From}/{To}", fromCurrency, toCurrency);
                return null;
            }
        }

        /// <summary>
        /// Fetches exchange rates for all configured currency pairs.
        /// </summary>
        public async Task<MarketDataViewModel> GetAllExchangeRatesAsync()
        {
            var cacheKey = "AV_AllExchangeRates";

            if (_cache.TryGetValue(cacheKey, out MarketDataViewModel? cached))
            {
                return cached!;
            }

            var result = new MarketDataViewModel();
            var pairs = _configuration.GetSection("AlphaVantage:DefaultCurrencyPairs").Get<string[]>()
                        ?? new[] { "USD/PHP", "USD/EUR", "USD/JPY" };

            foreach (var pair in pairs)
            {
                var parts = pair.Split('/');
                if (parts.Length != 2) continue;

                var rate = await GetExchangeRateAsync(parts[0], parts[1]);
                if (rate != null)
                {
                    result.ExchangeRates.Add(rate);
                }

                // Small delay between requests to avoid rate limiting
                await Task.Delay(300);
            }

            if (result.ExchangeRates.Any())
            {
                result.LastUpdated = result.ExchangeRates.Max(r => r.LastRefreshed);
            }
            else
            {
                result.HasError = true;
                result.ErrorMessage = "Unable to fetch exchange rates. Data will refresh automatically.";
            }

            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(CacheMinutes));
            return result;
        }

        // ════════════════════════════════════════════════════════
        // ECONOMIC INDICATORS
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// Fetches all economic indicators and returns an aggregated ViewModel.
        /// </summary>
        public async Task<EconomicDataViewModel> GetEconomicDataAsync()
        {
            var cacheKey = "AV_EconomicData";

            if (_cache.TryGetValue(cacheKey, out EconomicDataViewModel? cached))
            {
                return cached!;
            }

            var result = new EconomicDataViewModel();

            try
            {
                // Fetch all indicators with delays to respect rate limits
                result.GDP = await FetchIndicatorAsync("REAL_GDP", "annual", "Real GDP", "billions", "bi-bank", "navy");
                await Task.Delay(500);

                result.Inflation = await FetchIndicatorAsync("INFLATION", "annual", "Inflation (CPI)", "%", "bi-arrow-up-right", "amber");
                await Task.Delay(500);

                result.Unemployment = await FetchIndicatorAsync("UNEMPLOYMENT", "monthly", "Unemployment", "%", "bi-people", "red");
                await Task.Delay(500);

                result.InterestRate = await FetchIndicatorAsync("FEDERAL_FUNDS_RATE", "monthly", "Interest Rate", "%", "bi-percent", "green");

                result.LastUpdated = DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch economic indicators");
                result.HasError = true;
                result.ErrorMessage = "Unable to fetch economic data. Data will refresh automatically.";
            }

            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(CacheMinutes));
            return result;
        }

        /// <summary>
        /// Fetches a single economic indicator time series from Alpha Vantage.
        /// </summary>
        private async Task<EconomicIndicator> FetchIndicatorAsync(
            string function, string interval, string name, string unit, string icon, string colorClass)
        {
            var indicator = new EconomicIndicator
            {
                Name = name,
                Unit = unit,
                Icon = icon,
                ColorClass = colorClass
            };

            try
            {
                var url = $"{BaseUrl}?function={function}&interval={interval}&apikey={ApiKey}";
                var response = await _httpClient.GetStringAsync(url);
                var json = JsonDocument.Parse(response);

                // Check for API error/rate limit
                if (json.RootElement.TryGetProperty("Note", out _) ||
                    json.RootElement.TryGetProperty("Information", out _))
                {
                    _logger.LogWarning("Alpha Vantage rate limit hit for {Function}", function);
                    return indicator;
                }

                if (!json.RootElement.TryGetProperty("data", out var dataArray))
                {
                    _logger.LogWarning("No 'data' property in response for {Function}", function);
                    return indicator;
                }

                var points = new List<EconomicDataPoint>();
                foreach (var item in dataArray.EnumerateArray().Take(20)) // Last 20 data points
                {
                    if (item.TryGetProperty("date", out var dateProp) &&
                        item.TryGetProperty("value", out var valueProp))
                    {
                        var dateStr = dateProp.GetString();
                        var valueStr = valueProp.GetString();

                        if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                            decimal.TryParse(valueStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                        {
                            points.Add(new EconomicDataPoint { Date = date, Value = value });
                        }
                    }
                }

                indicator.TimeSeries = points.OrderBy(p => p.Date).ToList();

                if (points.Count > 0)
                {
                    // The API returns data newest-first, but we sorted ascending
                    // So latest is last, previous is second-to-last
                    var sorted = points.OrderByDescending(p => p.Date).ToList();
                    indicator.LatestValue = sorted[0].Value;
                    indicator.LatestDate = sorted[0].Date;
                    if (sorted.Count > 1)
                    {
                        indicator.PreviousValue = sorted[1].Value;
                    }
                }

                _logger.LogInformation("Fetched {Name}: latest = {Value}{Unit}", name, indicator.LatestValue, unit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch indicator {Name}", name);
            }

            return indicator;
        }

        // ════════════════════════════════════════════════════════
        // JSON HELPERS
        // ════════════════════════════════════════════════════════

        private static string GetJsonString(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() ?? "" : "";
        }

        private static decimal GetJsonDecimal(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                var str = prop.GetString();
                if (decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                    return val;
            }
            return 0;
        }

        private static DateTime GetJsonDateTime(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                var str = prop.GetString();
                if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.None, out var val))
                    return val;
            }
            return DateTime.MinValue;
        }
    }
}
