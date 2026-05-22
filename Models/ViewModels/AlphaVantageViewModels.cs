using System;
using System.Collections.Generic;

namespace FinSight.Models.ViewModels
{
    // ────────────────────────────────────────────────────────
    // EXCHANGE RATES
    // ────────────────────────────────────────────────────────

    /// <summary>
    /// Represents a single currency exchange rate from Alpha Vantage.
    /// </summary>
    public class ExchangeRateItem
    {
        public string FromCurrency { get; set; } = string.Empty;
        public string FromCurrencyName { get; set; } = string.Empty;
        public string ToCurrency { get; set; } = string.Empty;
        public string ToCurrencyName { get; set; } = string.Empty;
        public decimal Rate { get; set; }
        public decimal BidPrice { get; set; }
        public decimal AskPrice { get; set; }
        public DateTime LastRefreshed { get; set; }
        public string TimeZone { get; set; } = string.Empty;

        /// <summary>Display label, e.g. "USD → PHP"</summary>
        public string DisplayLabel => $"{FromCurrency} → {ToCurrency}";
    }

    /// <summary>
    /// Aggregated market data for Dashboard display.
    /// </summary>
    public class MarketDataViewModel
    {
        public List<ExchangeRateItem> ExchangeRates { get; set; } = new();
        public DateTime? LastUpdated { get; set; }
        public bool HasError { get; set; }
        public string? ErrorMessage { get; set; }
    }

    // ────────────────────────────────────────────────────────
    // ECONOMIC INDICATORS
    // ────────────────────────────────────────────────────────

    /// <summary>
    /// A single data point from an economic indicator time series.
    /// </summary>
    public class EconomicDataPoint
    {
        public DateTime Date { get; set; }
        public decimal Value { get; set; }
    }

    /// <summary>
    /// A named economic indicator with its time series data.
    /// </summary>
    public class EconomicIndicator
    {
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;          // e.g. "%", "billions"
        public string Icon { get; set; } = "bi-graph-up-arrow";   // Bootstrap icon class
        public string ColorClass { get; set; } = "tq";            // CSS accent class
        public decimal? LatestValue { get; set; }
        public DateTime? LatestDate { get; set; }
        public decimal? PreviousValue { get; set; }
        public List<EconomicDataPoint> TimeSeries { get; set; } = new();

        /// <summary>Change from previous value (positive = increase).</summary>
        public decimal? Change => (LatestValue.HasValue && PreviousValue.HasValue)
            ? LatestValue.Value - PreviousValue.Value
            : null;

        /// <summary>Change direction: "up", "down", or "flat".</summary>
        public string Trend => Change switch
        {
            > 0 => "up",
            < 0 => "down",
            _   => "flat"
        };
    }

    /// <summary>
    /// Aggregated economic data for the Forecast analytics page.
    /// </summary>
    public class EconomicDataViewModel
    {
        public EconomicIndicator GDP { get; set; } = new() { Name = "Real GDP", Unit = "billions", Icon = "bi-bank", ColorClass = "navy" };
        public EconomicIndicator Inflation { get; set; } = new() { Name = "Inflation (CPI)", Unit = "%", Icon = "bi-arrow-up-right", ColorClass = "amber" };
        public EconomicIndicator Unemployment { get; set; } = new() { Name = "Unemployment", Unit = "%", Icon = "bi-people", ColorClass = "red" };
        public EconomicIndicator InterestRate { get; set; } = new() { Name = "Interest Rate", Unit = "%", Icon = "bi-percent", ColorClass = "green" };

        public DateTime? LastUpdated { get; set; }
        public bool HasError { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>All indicators as a list for easy iteration.</summary>
        public List<EconomicIndicator> All => new() { GDP, Inflation, Unemployment, InterestRate };
    }
}
