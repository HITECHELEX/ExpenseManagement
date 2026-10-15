using System.ComponentModel.DataAnnotations;

namespace ExpenseManagement.Models
{
    public class Holiday
    {
        public int Id { get; set; }

        [Required]
        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        // Date only (stored as a Postgres "date")
        public DateTime HolidayDate { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}