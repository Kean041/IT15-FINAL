using System;
using System.Security.Cryptography;
using System.Text;

namespace FinSight.Helpers
{
    /// <summary>
    /// Secure password hashing utility using PBKDF2 (HMAC-SHA256).
    /// Supports dual-hash migration from legacy SHA256 hashes.
    /// </summary>
    public static class PasswordHelper
    {
        // PBKDF2 configuration
        private const int SaltSize = 16;       // 128-bit salt
        private const int HashSize = 32;       // 256-bit hash
        private const int Iterations = 100_000; // OWASP recommended minimum
        private const string Prefix = "PBKDF2$"; // Marker to identify new hashes

        /// <summary>
        /// Hashes a plain-text password using PBKDF2 with a random salt.
        /// Returns a prefixed string: "PBKDF2$iterations$salt$hash"
        /// </summary>
        public static string HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize
            );

            return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        /// <summary>
        /// Verifies a plain-text password against a stored hash.
        /// Supports both new PBKDF2 hashes and legacy SHA256 hashes.
        /// </summary>
        public static bool VerifyPassword(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash))
                return false;

            if (storedHash.StartsWith(Prefix))
            {
                return VerifyPbkdf2(password, storedHash);
            }

            // Legacy fallbacks for older/manual demo databases.
            // Successful legacy logins are upgraded to PBKDF2 by AuthController.
            return VerifyLegacySha256(password, storedHash) || VerifyLegacyPlainText(password, storedHash);
        }

        /// <summary>
        /// Returns true if the stored hash is a legacy SHA256 hash
        /// that should be upgraded to PBKDF2 on next successful login.
        /// </summary>
        public static bool IsLegacyHash(string storedHash)
        {
            return !string.IsNullOrEmpty(storedHash) && !storedHash.StartsWith(Prefix);
        }

        // ── PBKDF2 verification ──
        private static bool VerifyPbkdf2(string password, string storedHash)
        {
            try
            {
                // Format: "PBKDF2$iterations$salt$hash"
                var parts = storedHash.Split('$');
                if (parts.Length != 4) return false;

                int iterations = int.Parse(parts[1]);
                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expectedHash = Convert.FromBase64String(parts[3]);

                byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expectedHash.Length
                );

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch
            {
                return false;
            }
        }

        // ── Legacy SHA256 verification (for migration) ──
        private static bool VerifyLegacySha256(string password, string storedHash)
        {
            if (!IsSha256HexHash(storedHash))
                return false;

            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            var sb = new StringBuilder();
            foreach (var b in bytes)
                sb.Append(b.ToString("x2"));

            return string.Equals(sb.ToString(), storedHash, StringComparison.OrdinalIgnoreCase);
        }

        private static bool VerifyLegacyPlainText(string password, string storedHash)
        {
            if (IsSha256HexHash(storedHash))
                return false;

            return string.Equals(password, storedHash, StringComparison.Ordinal);
        }

        private static bool IsSha256HexHash(string storedHash)
        {
            return storedHash.Length == 64 && storedHash.All(Uri.IsHexDigit);
        }
    }
}
