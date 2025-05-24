// using System.Security.Claims;
// using Microsoft.AspNetCore.Authorization;
// using Microsoft.AspNetCore.Mvc;
// using MyTts.Models.Auth;
// using MyTts.Services.Interfaces;
// namespace MyTts.Controllers
// {

//     [ApiController]
//     [Route("api/[controller]")]
//     public class AuthController : ControllerBase
//     {
//         private readonly IAuthService _authService;
//         private readonly ILogger<AuthController> _logger;

//         public AuthController(IAuthService authService, ILogger<AuthController> logger)
//         {
//             _authService = authService;
//             _logger = logger;
//         }

//         [HttpPost("register")]
//         public async Task<IActionResult> Register([FromBody] RegisterRequest request)
//         {
//             if (!ModelState.IsValid)
//             {
//                 return BadRequest(ModelState);
//             }

//             var result = await _authService.RegisterAsync(request);
//             if (result == null)
//             {
//                 return BadRequest(new { message = "Registration failed" });
//             }

//             return Ok(result);
//         }

//         [HttpPost("login")]
//         public async Task<IActionResult> Login([FromBody] LoginRequest request)
//         {
//             if (!ModelState.IsValid)
//             {
//                 return BadRequest(ModelState);
//             }

//             var result = await _authService.LoginAsync(request);
//             if (result == null)
//             {
//                 return Unauthorized(new { message = "Invalid credentials" });
//             }

//             return Ok(result);
//         }

//         [HttpPost("refresh")]
//         public async Task<IActionResult> RefreshToken([FromBody] string refreshToken)
//         {
//             var result = await _authService.RefreshTokenAsync(refreshToken);
//             if (result == null)
//             {
//                 return Unauthorized(new { message = "Invalid refresh token" });
//             }

//             return Ok(result);
//         }

//         [HttpPost("revoke")]
//         [Authorize]
//         public async Task<IActionResult> RevokeToken([FromBody] string refreshToken)
//         {
//             await _authService.RevokeTokenAsync(refreshToken);
//             return Ok(new { message = "Token revoked successfully" });
//         }

//         [HttpGet("me")]
//         [Authorize]
//         public async Task<IActionResult> GetCurrentUser()
//         {
//             var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
//             if (string.IsNullOrEmpty(userId))
//             {
//                 return Unauthorized();
//             }

//             var userInfo = await _authService.GetUserInfoAsync(userId);
//             if (userInfo == null)
//             {
//                 return NotFound();
//             }

//             return Ok(userInfo);
//         }
//     }
// }