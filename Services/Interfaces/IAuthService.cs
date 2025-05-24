using MyTts.Models.Auth;

namespace MyTts.Services.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponse?> LoginAsync(LoginRequest request);
        Task<AuthResponse?> RegisterAsync(RegisterRequest request);
        Task<AuthResponse?> RefreshTokenAsync(string refreshToken);
        Task<bool> RevokeTokenAsync(string refreshToken);
        Task<UserInfo?> GetUserInfoAsync(string userId);
    }

    public interface IJwtTokenService
    {
        Task<string> GenerateTokenAsync(ApplicationUser user);
        string GenerateRefreshToken();
        Task<bool> ValidateRefreshTokenAsync(string refreshToken, string userId);
    }
}
