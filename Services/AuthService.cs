using Microsoft.AspNetCore.Identity;
using MyTts.Models.Auth;
using MyTts.Services.Interfaces;
namespace MyTts.Services
{
    
public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtTokenService jwtTokenService,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        try
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null || !user.IsActive)
            {
                return null;
            }

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, true);
            if (!result.Succeeded)
            {
                return null;
            }

            // Update last login
            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);

            var token = await _jwtTokenService.GenerateTokenAsync(user);
            var refreshToken = _jwtTokenService.GenerateRefreshToken();

            var roles = await _userManager.GetRolesAsync(user);

            return new AuthResponse
            {
                Token = token,
                RefreshToken = refreshToken,
                Expires = DateTime.UtcNow.AddHours(24),
                User = new UserInfo
                {
                    Id = user.Id,
                    Email = user.Email!,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Roles = roles.ToList()
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for user {Email}", request.Email);
            return null;
        }
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
    {
        try
        {
            var user = new ApplicationUser
            {
                UserName = request.Email,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                EmailConfirmed = true // For API, we skip email confirmation
            };

            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                _logger.LogWarning("User registration failed for {Email}: {Errors}", 
                    request.Email, string.Join(", ", result.Errors.Select(e => e.Description)));
                return null;
            }

            // Assign default role
            await _userManager.AddToRoleAsync(user, "User");

            var token = await _jwtTokenService.GenerateTokenAsync(user);
            var refreshToken = _jwtTokenService.GenerateRefreshToken();

            return new AuthResponse
            {
                Token = token,
                RefreshToken = refreshToken,
                Expires = DateTime.UtcNow.AddHours(24),
                User = new UserInfo
                {
                    Id = user.Id,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    Roles = new List<string> { "User" }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for user {Email}", request.Email);
            return null;
        }
    }

    public async Task<AuthResponse?> RefreshTokenAsync(string refreshToken)
    {
        // Implementation for refresh token logic
        // This would typically involve validating the refresh token and generating new tokens
        await Task.CompletedTask;
        return null; // Implement based on your needs
    }

    public async Task<bool> RevokeTokenAsync(string refreshToken)
    {
        // Implementation for token revocation
        await Task.CompletedTask;
        return true;
    }

    public async Task<UserInfo?> GetUserInfoAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return null;

        var roles = await _userManager.GetRolesAsync(user);

        return new UserInfo
        {
            Id = user.Id,
            Email = user.Email!,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Roles = roles.ToList()
        };
    }
}
}