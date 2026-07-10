using System;

namespace SitePass.Core.Entities
{
    public class SavedGuest
    {
        public int Id { get; set; }
        public string Plate { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        
        public int ResidentId { get; set; }
        public virtual User? Resident { get; set; }
    }
}
