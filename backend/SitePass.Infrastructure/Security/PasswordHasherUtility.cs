using Microsoft.AspNetCore.Identity;
using SitePass.Core.Entities;

namespace SitePass.Infrastructure.Security
{
    public static class PasswordHasherUtility
    {
        private static readonly PasswordHasher<User> Hasher = new PasswordHasher<User>();

        public static string HashPassword(User user, string password)
        {
            return Hasher.HashPassword(user, password);
        }

        public static bool VerifyPassword(User user, string hashedPassword, string providedPassword)
        {
            var result = Hasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
            return result == PasswordVerificationResult.Success || 
                   result == PasswordVerificationResult.SuccessRehashNeeded;
        }
    }
}
