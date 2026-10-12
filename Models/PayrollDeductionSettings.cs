using System.ComponentModel.DataAnnotations;

namespace ExpenseManagement.Models
{
    public class PayrollDeductionSettings
    {
        [Range(0, 999999999)]
        public decimal? PF { get; set; }

        [Range(0, 999999999)]
        public decimal? ESIC { get; set; }

        [Range(0, 999999999)]
        public decimal? ProfessionalTax { get; set; }

        [Range(0, 999999999)]
        public decimal? LWF { get; set; }
    }
}