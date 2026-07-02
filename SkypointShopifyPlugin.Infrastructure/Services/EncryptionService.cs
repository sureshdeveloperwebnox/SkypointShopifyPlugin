using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SkypointShopifyPlugin.Core.Interfaces;

namespace SkypointShopifyPlugin.Infrastructure.Services
{
    public class EncryptionService : IEncryptionService
    {
        private readonly byte[] _encryptionKey;
        private readonly ILogger<EncryptionService> _logger;

        public EncryptionService(IConfiguration configuration, ILogger<EncryptionService> logger)
        {
            _logger = logger;
            _encryptionKey = ResolveEncryptionKey(configuration);
        }

        public (string EncryptedBase64, string IvBase64) Encrypt(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext))
                throw new ArgumentException("Plaintext cannot be empty", nameof(plaintext));

            var iv = RandomNumberGenerator.GetBytes(12); // 96-bit nonce
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[16]; // 128-bit authentication tag

            using var aes = new AesGcm(_encryptionKey, 16);
            aes.Encrypt(iv, plaintextBytes, ciphertext, tag);

            var combined = new byte[ciphertext.Length + tag.Length];
            ciphertext.CopyTo(combined, 0);
            tag.CopyTo(combined, ciphertext.Length);

            return (Convert.ToBase64String(combined), Convert.ToBase64String(iv));
        }

        public string Decrypt(string encryptedBase64, string ivBase64)
        {
            if (string.IsNullOrEmpty(encryptedBase64))
                throw new ArgumentException("Encrypted content cannot be empty", nameof(encryptedBase64));
            if (string.IsNullOrEmpty(ivBase64))
                throw new ArgumentException("IV cannot be empty", nameof(ivBase64));

            var combined = Convert.FromBase64String(encryptedBase64);
            var iv = Convert.FromBase64String(ivBase64);
            var ciphertext = combined[..^16];
            var tag = combined[^16..];
            var plaintext = new byte[ciphertext.Length];

            using var aes = new AesGcm(_encryptionKey, 16);
            aes.Decrypt(iv, ciphertext, tag, plaintext);

            return Encoding.UTF8.GetString(plaintext);
        }

        private byte[] ResolveEncryptionKey(IConfiguration configuration)
        {
            // 1. Try resolving key from environment or configuration
            var envKey = configuration["EncryptionKey"] ?? Environment.GetEnvironmentVariable("ENCRYPTION_KEY");
            if (!string.IsNullOrEmpty(envKey))
            {
                try
                {
                    // Try parsing as Base64 first
                    var bytes = Convert.FromBase64String(envKey);
                    if (bytes.Length == 32)
                    {
                        _logger.LogInformation("Loaded 256-bit encryption key from configuration/environment variables.");
                        return bytes;
                    }
                }
                catch
                {
                    // Fallback: parse as UTF8 and hash/pad to 32 bytes
                    using var sha256 = SHA256.Create();
                    var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(envKey));
                    _logger.LogInformation("Derived 256-bit encryption key from hashed configuration string.");
                    return hashedBytes;
                }
            }

            // 2. Fallback: load or create key file in the data directory
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }

            var keyFile = Path.Combine(dataDir, "skypoint_key.bin");
            if (File.Exists(keyFile))
            {
                var existing = File.ReadAllBytes(keyFile);
                if (existing.Length == 32)
                {
                    return existing;
                }
                _logger.LogWarning("Encryption key file corrupt or wrong size — regenerating key file.");
            }

            // Generate new random key
            var key = RandomNumberGenerator.GetBytes(32);
            try
            {
                File.WriteAllBytes(keyFile, key);
                _logger.LogInformation("Generated new persistent encryption key file at {Path}", keyFile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write encryption key file to disk. Using in-memory ephemeral key.");
            }
            return key;
        }
    }
}
