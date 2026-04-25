using System;

namespace FinSight.Models
{
    public class VarianceRecord
    {
        public int Id { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal BudgetAmount { get; set; }
        public decimal ActualExpense { get; set; }
        public decimal VarianceAmount { get; set; }
        public string Status { get; set; } = string.Empty; // "Over Budget" or "Under Budget"
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
    }
}
