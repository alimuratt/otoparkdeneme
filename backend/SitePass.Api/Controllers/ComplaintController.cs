using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SitePass.Core.Entities;
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
    public class ComplaintController : ControllerBase
    {
        private readonly SitePassDbContext _context;

        public ComplaintController(SitePassDbContext context)
        {
            _context = context;
        }

        public class CreateComplaintRequest
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

            // Word count limit validation (300 words)
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
                    ResidentName = c.Resident != null ? $"{c.Resident.FirstName} {c.Resident.LastName}" : "Bilinmiyor"
                })
                .ToListAsync();

            return Ok(list);
        }
    }
}
