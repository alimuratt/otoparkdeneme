using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SitePass.Api.Hubs;
using SitePass.Core.Entities;
using SitePass.Infrastructure.Data;
using SitePass.Infrastructure.Services;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SitePass.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ComplaintController : ControllerBase
    {
        private readonly SitePassDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly PushNotificationService _pushService;

        public ComplaintController(
            SitePassDbContext context,
            IHubContext<NotificationHub> hubContext,
            PushNotificationService pushService)
        {
            _context = context;
            _hubContext = hubContext;
            _pushService = pushService;
        }

        public class CreateComplaintRequest
        {
            public string Text { get; set; } = string.Empty;
        }

        public class ReplyComplaintRequest
        {
            public string Text { get; set; } = string.Empty;
        }

        [HttpPost]
        [Authorize(Roles = "Resident")]
        public async Task<IActionResult> CreateComplaint([FromBody] CreateComplaintRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest(new { Message = "Şikayet metni boş olamaz." });
            }

            var words = request.Text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length > 300)
            {
                return BadRequest(new { Message = "Şikayetiniz en fazla 300 kelime olabilir." });
            }

            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return Unauthorized(new { Message = "Geçersiz oturum." });
            }

            var complaint = new Complaint
            {
                Text = request.Text,
                CreatedDate = DateTime.Now,
                ResidentId = userId
            };

            _context.Complaints.Add(complaint);
            await _context.SaveChangesAsync();

            return Ok(new { Success = true, Message = "Şikayetiniz başarıyla iletildi." });
        }

        [HttpGet("my-list")]
        [Authorize(Roles = "Resident")]
        public async Task<IActionResult> GetMyList()
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return Unauthorized(new { Message = "Geçersiz oturum." });
            }

            var list = await _context.Complaints
                .Where(c => c.ResidentId == userId)
                .OrderByDescending(c => c.CreatedDate)
                .Select(c => new
                {
                    c.Id,
                    c.Text,
                    c.CreatedDate,
                    c.ReplyText,
                    c.ReplyDate
                })
                .ToListAsync();

            return Ok(list);
        }

        [HttpDelete("{id}")]
        [Authorize(Roles = "Resident")]
        public async Task<IActionResult> WithdrawComplaint(int id)
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
            {
                return Unauthorized(new { Message = "Geçersiz oturum." });
            }

            var complaint = await _context.Complaints.FindAsync(id);
            if (complaint == null)
            {
                return NotFound(new { Message = "Şikayet bulunamadı." });
            }

            // Only owner can withdraw their complaint
            if (complaint.ResidentId != userId)
            {
                return Forbid();
            }

            _context.Complaints.Remove(complaint);
            await _context.SaveChangesAsync();

            return Ok(new { Success = true, Message = "Şikayetiniz başarıyla geri çekildi." });
        }

        [HttpGet("admin-list")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAdminList()
        {
            var list = await _context.Complaints
                .Include(c => c.Resident)
                .OrderByDescending(c => c.CreatedDate)
                .Select(c => new
                {
                    c.Id,
                    c.Text,
                    c.CreatedDate,
                    BlockNo = c.Resident != null ? c.Resident.BlockNo : "Bilinmiyor",
                    ApartmentNo = c.Resident != null ? c.Resident.ApartmentNo : "Bilinmiyor",
                    ResidentName = c.Resident != null ? $"{c.Resident.FirstName} {c.Resident.LastName}" : "Bilinmiyor",
                    c.ReplyText,
                    c.ReplyDate
                })
                .ToListAsync();

            return Ok(list);
        }

        [HttpPost("reply/{id}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ReplyComplaint(int id, [FromBody] ReplyComplaintRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest(new { Message = "Cevap metni boş olamaz." });
            }

            var complaint = await _context.Complaints.FindAsync(id);
            if (complaint == null)
            {
                return NotFound(new { Message = "Şikayet bulunamadı." });
            }

            complaint.ReplyText = request.Text;
            complaint.ReplyDate = DateTime.Now;

            _context.Entry(complaint).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            // Trigger SignalR notification to the resident
            try
            {
                await _hubContext.Clients.User(complaint.ResidentId.ToString()).SendAsync("ReceiveNotification", new
                {
                    Title = "🔔 Şikayetinize Cevap Verildi",
                    Message = "Yönetici şikayetinize yanıt verdi. Şikayetlerim sekmesinden okuyabilirsiniz.",
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SignalR] Failed to send complaint reply notification: {ex.Message}");
            }

            // Send native push notification
            try
            {
                await _pushService.SendNotificationToUserAsync(
                    complaint.ResidentId,
                    "🔔 Şikayetinize Cevap Verildi",
                    "Yönetici şikayetinize yanıt verdi. Şikayetlerim sekmesinden okuyabilirsiniz."
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WebPush] Failed to send complaint reply push notification: {ex.Message}");
            }

            return Ok(new { Success = true, Message = "Cevabınız başarıyla iletildi." });
        }
    }
}
