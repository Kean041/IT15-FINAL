namespace FinSight.Models.ViewModels
{
    /// <summary>
    /// ViewModel used to present computed variance analysis results
    /// comparing budgeted amounts against actual expenses per budget line.
    /// </summary>
    public class VarianceAnalysisViewModel
    {
        public int BudgetID { get; set; }
        public int DepartmentID { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal BudgetAmount { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal Variance { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Year { get; set; }
    }
}
