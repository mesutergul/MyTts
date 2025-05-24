using Microsoft.AspNetCore.Identity;
namespace MyTts.Models.Auth
{
    public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
    
    // API usage tracking
    public int DailyRequestCount { get; set; }
    public DateTime LastRequestDate { get; set; } = DateTime.UtcNow.Date;
    public int DailyRequestLimit { get; set; } = 1000; // Default limit
}
}