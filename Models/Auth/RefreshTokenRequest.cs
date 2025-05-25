using System.ComponentModel.DataAnnotations;

namespace MyTts.Models.Auth
{
    public class RefreshTokenRequest
    {
        [Required]
        public string Token { get; set; } = string.Empty;

        [Required]
        public string RefreshToken { get; set; } = string.Empty;
    }
}