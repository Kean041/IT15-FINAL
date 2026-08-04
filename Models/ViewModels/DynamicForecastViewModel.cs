namespace FinSight.Models.ViewModels
{
    public class DynamicForecastViewModel
    {
        public int BudgetID { get; set; }
        public int DepartmentID { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal BudgetAmount { get; set; }
        public decimal CurrentExpenses { get; set; }
        public decimal CurrentUtilization { get; set; }
        public decimal RunRate { get; set; }
        public decimal AppliedInflationRate { get; set; }
        public decimal FutureExpenses { get; set; }
        public decimal FutureBudgetUtilization { get; set; }
        public decimal ProjectedRemainingBudget { get; set; }
        public decimal PredictedVariance { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Year { get; set; }
    }
}
