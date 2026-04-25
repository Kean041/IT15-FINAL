using System.Security.Cryptography;
using System.Text;

namespace FinSight.Helpers
{
    /// <summary>
    /// Shared password hashing utility.
    /// Extracted from AuthController so multiple controllers can reuse the same logic.
    /// </summary>
    public static class PasswordHelper
    {
        /// <summary>
        /// Hashes a plain-text password using SHA256 and returns the hex string.
        /// </summary>
        public static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            var sb = new StringBuilder();
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
