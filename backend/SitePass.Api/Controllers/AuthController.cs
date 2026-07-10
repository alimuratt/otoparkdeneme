using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SitePass.Core.Entities;
using SitePass.Core.Enums;
using SitePass.Infrastructure.Data;
using SitePass.Infrastructure.Security;
using SitePass.Infrastructure.Services;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace SitePass.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly SitePassDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly PushNotificationService _pushService;

        public AuthController(SitePassDbContext context, IConfiguration configuration, PushNotificationService pushService)
        {
            _context = context;
            _configuration = configuration;
            _pushService = pushService;
        }

        public class LoginRequest
        {
            public string PhoneNumber { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }



        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PhoneNumber) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { Message = "Telefon numarası ve şifre gereklidir." });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber);
            if (user == null)
            {
                return Unauthorized(new { Message = "Geçersiz telefon numarası veya şifre." });
            }

            var isPasswordValid = PasswordHasherUtility.VerifyPassword(user, user.PasswordHash, request.Password);
            if (!isPasswordValid)
            {
                return Unauthorized(new { Message = "Geçersiz telefon numarası veya şifre." });
            }

            // Generate JWT Token
            var jwtSettings = _configuration.GetSection("Jwt");
            var keyString = jwtSettings.GetValue<string>("Key") ?? "SuperSecretKeyForSitePassProjectAuthentication2026!";
            var issuer = jwtSettings.GetValue<string>("Issuer") ?? "SitePassApi";
            var audience = jwtSettings.GetValue<string>("Audience") ?? "SitePassClient";
            var expireMinutes = jwtSettings.GetValue<int>("ExpireMinutes", 1440);

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(keyString);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
                    new Claim(ClaimTypes.Role, user.Role.ToString()),
                    new Claim("BlockNo", user.BlockNo ?? ""),
                    new Claim("ApartmentNo", user.ApartmentNo ?? ""),
                    new Claim("PhoneNumber", user.PhoneNumber)
                }),
                Expires = DateTime.UtcNow.AddMinutes(expireMinutes),
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            var tokenString = tokenHandler.WriteToken(token);

            return Ok(new
            {
                Token = tokenString,
                ExpiresAt = tokenDescriptor.Expires,
                User = new
                {
                    user.Id,
                    Name = $"{user.FirstName} {user.LastName}",
                    user.PhoneNumber,
                    Role = user.Role.ToString(),
                    user.BlockNo,
                    user.ApartmentNo
                }
            });
        }

        [HttpGet("vapid-public-key")]
        public IActionResult GetVapidPublicKey()
        {
            var publicKey = _pushService.GetPublicKey();
            return Ok(new { PublicKey = publicKey });
        }

        [HttpPost("subscribe-push")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<IActionResult> SubscribePush([FromBody] PushSubscriptionRequest request)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var userId))
            {
                return Unauthorized(new { Message = "Kullanıcı kimliği geçersiz." });
            }

            if (request == null || string.IsNullOrEmpty(request.Endpoint) || string.IsNullOrEmpty(request.P256dh) || string.IsNullOrEmpty(request.Auth))
            {
                return BadRequest(new { Message = "Geçersiz push abonelik verisi." });
            }

            await _pushService.SaveSubscriptionAsync(userId, request.Endpoint, request.P256dh, request.Auth);
            return Ok(new { Success = true, Message = "Push bildirim aboneliği başarıyla kaydedildi." });
        }

        public class PushSubscriptionRequest
        {
            public string Endpoint { get; set; } = string.Empty;
            public string P256dh { get; set; } = string.Empty;
            public string Auth { get; set; } = string.Empty;
        }
    }
}
