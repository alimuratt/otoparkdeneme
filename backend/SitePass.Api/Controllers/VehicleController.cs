using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SitePass.Api.Hubs;
using SitePass.Core.Entities;
using SitePass.Core.Enums;
using SitePass.Infrastructure.Data;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SitePass.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VehicleController : ControllerBase
    {
        private readonly SitePassDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public VehicleController(SitePassDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public class AddGuestVehicleRequest
        {
            public string Plate { get; set; } = string.Empty;
        }

        // Normalize plate: remove spaces, convert to uppercase with Turkish support
        private string NormalizePlate(string plate)
        {
            if (string.IsNullOrWhiteSpace(plate)) return string.Empty;
            
            // Remove all spaces
            var result = plate.Replace(" ", "");
            
            // Turkish culture-aware uppercase
            return result.ToUpper(new System.Globalization.CultureInfo("tr-TR"));
        }

        [HttpPost("guest")]
        [Authorize(Roles = "Resident")]
        public async Task<IActionResult> AddGuestVehicle([FromBody] AddGuestVehicleRequest request)
        {
            var cleanPlate = NormalizePlate(request.Plate);
            if (string.IsNullOrEmpty(cleanPlate))
            {
                return BadRequest(new { Message = "Plaka boş olamaz." });
            }

            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var residentId))
            {
                return Unauthorized(new { Message = "Kullanıcı kimliği geçersiz." });
            }

            // Check if the plate is already registered as an active vehicle
            var existingActive = await _context.Vehicles
                .FirstOrDefaultAsync(v => v.Plate == cleanPlate && v.IsActive);

            if (existingActive != null)
            {
                if (existingActive.IsGuest)
                {
                    return BadRequest(new { Message = $"Bu plaka zaten aktif bir misafir olarak kayıtlı. Kalan Süre: {existingActive.ExpireDate}" });
                }
                else
                {
                    return BadRequest(new { Message = "Bu plaka zaten kalıcı/sabit araç olarak kayıtlı." });
                }
            }

            var guestVehicle = new Vehicle
            {
                Plate = cleanPlate,
                ResidentId = residentId,
                IsGuest = true,
                IsActive = true,
                CreatedDate = DateTime.UtcNow,
                ExpireDate = DateTime.UtcNow.AddHours(12) // 12 hours validity
            };

            _context.Vehicles.Add(guestVehicle);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                Message = "Misafir araç başarıyla tanımlandı. 12 saat geçerlidir.",
                Vehicle = new
                {
                    guestVehicle.Id,
                    guestVehicle.Plate,
                    guestVehicle.IsGuest,
                    guestVehicle.CreatedDate,
                    guestVehicle.ExpireDate,
                    guestVehicle.IsActive
                }
            });
        }

        [HttpGet("active-guests")]
        [Authorize(Roles = "Resident")]
        public async Task<IActionResult> GetActiveGuests()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var residentId))
            {
                return Unauthorized(new { Message = "Kullanıcı kimliği geçersiz." });
            }

            var now = DateTime.UtcNow;
            var activeGuests = await _context.Vehicles
                .Where(v => v.ResidentId == residentId && v.IsActive && v.IsGuest && (v.ExpireDate == null || v.ExpireDate > now))
                .Select(v => new
                {
                    v.Id,
                    v.Plate,
                    v.CreatedDate,
                    v.ExpireDate,
                    RemainingSeconds = v.ExpireDate.HasValue ? (int)Math.Max(0, (v.ExpireDate.Value - now).TotalSeconds) : 0
                })
                .ToListAsync();

            return Ok(activeGuests);
        }

        // Mock LPR (License Plate Recognition) trigger endpoint
        [HttpPost("mock-lpr")]
        [AllowAnonymous] // Allow simulation without JWT for convenience
        public async Task<IActionResult> MockLpr([FromBody] AddGuestVehicleRequest request)
        {
            var cleanPlate = NormalizePlate(request.Plate);
            if (string.IsNullOrEmpty(cleanPlate))
            {
                return BadRequest(new { Message = "Plaka boş olamaz." });
            }

            var now = DateTime.UtcNow;
            
            // Check if the plate is active (either guest with valid time, or permanent)
            var vehicle = await _context.Vehicles
                .Include(v => v.Resident)
                .FirstOrDefaultAsync(v => v.Plate == cleanPlate && v.IsActive && 
                                         (!v.IsGuest || v.ExpireDate == null || v.ExpireDate > now));

            if (vehicle == null)
            {
                return BadRequest(new { Success = false, Message = $"Geçersiz veya süresi dolmuş plaka: {cleanPlate}. Bariyer açılmadı!" });
            }

            // Plate is valid! Trigger SignalR notification
            var residentIdString = vehicle.ResidentId.ToString();
            var residentName = $"{vehicle.Resident.FirstName} {vehicle.Resident.LastName}";
            
            string title = "🚗 Araç Girişi Gerçekleşti";
            string message = vehicle.IsGuest 
                ? $"Tanımladığınız {cleanPlate} plakalı misafir aracınız siteye giriş yapmıştır."
                : $"{cleanPlate} plakalı kalıcı aracınız siteye giriş yapmıştır.";

            // Send via SignalR
            await _hubContext.Clients.User(residentIdString).SendAsync("ReceiveNotification", new
            {
                Title = title,
                Message = message,
                Timestamp = DateTime.UtcNow,
                Plate = cleanPlate
            });

            return Ok(new
            {
                Success = true,
                Message = $"Bariyer Açıldı! Sakine ({residentName}) bildirim gönderildi.",
                Plate = cleanPlate,
                Resident = residentName,
                IsGuest = vehicle.IsGuest
            });
        }

        [HttpPost("cancel/{id}")]
        [Authorize(Roles = "Resident")]
        public async Task<IActionResult> CancelGuestVehicle(int id)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var residentId))
            {
                return Unauthorized(new { Message = "Kullanıcı kimliği geçersiz." });
            }

            var vehicle = await _context.Vehicles.FindAsync(id);

            if (vehicle == null)
            {
                return NotFound(new { Message = "Araç kaydı bulunamadı." });
            }

            if (vehicle.ResidentId != residentId)
            {
                return Forbid();
            }

            if (!vehicle.IsGuest)
            {
                return BadRequest(new { Message = "Sadece misafir araçlar iptal edilebilir." });
            }

            if (!vehicle.IsActive)
            {
                return BadRequest(new { Message = "Bu araç zaten aktif değil." });
            }

            vehicle.IsActive = false;
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Misafir araç izni başarıyla iptal edildi." });
        }
    }
}
