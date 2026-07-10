using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using SitePass.Core.Entities;
using SitePass.Core.Enums;
using SitePass.Infrastructure.Data;
using SitePass.Infrastructure.Security;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SitePass.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly SitePassDbContext _context;

        public AdminController(SitePassDbContext context)
        {
            _context = context;
        }

        #region User Management DTOs
        public class CreateUserRequest
        {
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string? BlockNo { get; set; }
            public string? ApartmentNo { get; set; }
            public string PhoneNumber { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string Role { get; set; } = "Resident"; // Resident, Security, Admin
        }
        #endregion

        #region Vehicle Management DTOs
        public class AddPermanentVehicleRequest
        {
            public string Plate { get; set; } = string.Empty;
            public int ResidentId { get; set; }
        }
        #endregion

        #region User Management Endpoints

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            var users = await _context.Users
                .Select(u => new
                {
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    u.BlockNo,
                    u.ApartmentNo,
                    u.PhoneNumber,
                    Role = u.Role.ToString()
                })
                .ToListAsync();

            return Ok(users);
        }

        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.PhoneNumber) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { Message = "Telefon ve şifre gereklidir." });
            }

            var phoneExists = await _context.Users.AnyAsync(u => u.PhoneNumber == request.PhoneNumber);
            if (phoneExists)
            {
                return BadRequest(new { Message = "Bu telefon numarasıyla kayıtlı bir kullanıcı zaten mevcut." });
            }

            if (!Enum.TryParse<UserRole>(request.Role, true, out var userRole))
            {
                return BadRequest(new { Message = "Geçersiz rol tanımı." });
            }

            var user = new User
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                BlockNo = request.BlockNo,
                ApartmentNo = request.ApartmentNo,
                PhoneNumber = request.PhoneNumber,
                Role = userRole
            };

            user.PasswordHash = PasswordHasherUtility.HashPassword(user, request.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                Message = "Kullanıcı başarıyla kaydedildi.",
                User = new
                {
                    user.Id,
                    user.FirstName,
                    user.LastName,
                    user.BlockNo,
                    user.ApartmentNo,
                    user.PhoneNumber,
                    Role = user.Role.ToString()
                }
            });
        }

        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { Message = "Kullanıcı bulunamadı." });
            }

            // Don't let admin delete their own account from here
            var currentUserId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (currentUserId == id.ToString())
            {
                return BadRequest(new { Message = "Kendi yöneticilik hesabınızı silemezsiniz." });
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Kullanıcı başarıyla silindi." });
        }

        #endregion

        #region Vehicle Management Endpoints

        [HttpGet("vehicles")]
        public async Task<IActionResult> GetVehicles()
        {
            var vehicles = await _context.Vehicles
                .Include(v => v.Resident)
                .Select(v => new
                {
                    v.Id,
                    v.Plate,
                    v.IsGuest,
                    v.IsActive,
                    v.CreatedDate,
                    v.ExpireDate,
                    ResidentName = $"{v.Resident.FirstName} {v.Resident.LastName}",
                    BlockNo = v.Resident.BlockNo ?? "",
                    ApartmentNo = v.Resident.ApartmentNo ?? ""
                })
                .ToListAsync();

            return Ok(vehicles);
        }

        [HttpPost("vehicles")]
        public async Task<IActionResult> AddPermanentVehicle([FromBody] AddPermanentVehicleRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Plate))
            {
                return BadRequest(new { Message = "Plaka boş bırakılamaz." });
            }

            var resident = await _context.Users.FindAsync(request.ResidentId);
            if (resident == null)
            {
                return BadRequest(new { Message = "Belirtilen daire sakini bulunamadı." });
            }

            if (resident.Role != UserRole.Resident)
            {
                return BadRequest(new { Message = "Araç sadece 'Site Sakini' rolündeki kullanıcılara tanımlanabilir." });
            }

            // Normalize plate
            var cleanPlate = request.Plate.Replace(" ", "").ToUpper(new System.Globalization.CultureInfo("tr-TR"));

            // Check if active duplicate exists
            var existing = await _context.Vehicles.FirstOrDefaultAsync(v => v.Plate == cleanPlate && v.IsActive);
            if (existing != null)
            {
                return BadRequest(new { Message = $"Bu plaka ({cleanPlate}) zaten aktif olarak kayıtlıdır." });
            }

            var vehicle = new Vehicle
            {
                Plate = cleanPlate,
                ResidentId = request.ResidentId,
                IsGuest = false, // Permanent
                IsActive = true,
                CreatedDate = DateTime.UtcNow
            };

            _context.Vehicles.Add(vehicle);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                Message = "Kalıcı araç başarıyla tanımlandı.",
                Vehicle = new
                {
                    vehicle.Id,
                    vehicle.Plate,
                    vehicle.IsGuest,
                    vehicle.IsActive,
                    vehicle.CreatedDate,
                    ResidentName = $"{resident.FirstName} {resident.LastName}"
                }
            });
        }

        [HttpDelete("vehicles/{id}")]
        public async Task<IActionResult> RemoveVehicle(int id)
        {
            var vehicle = await _context.Vehicles.FindAsync(id);
            if (vehicle == null)
            {
                return NotFound(new { Message = "Araç bulunamadı." });
            }

            _context.Vehicles.Remove(vehicle);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Araç sistemden başarıyla kaldırıldı." });
        }

        #endregion
    }
}
