using System;

namespace SitePass.Core.Entities
{
    public class Complaint
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        
        public int ResidentId { get; set; }
        public virtual User? Resident { get; set; }
    }
}
