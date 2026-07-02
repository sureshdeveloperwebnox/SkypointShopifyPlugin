namespace SkypointShopifyPlugin.Core.Interfaces
{
    /// <summary>
    /// Centralized encryption service for security credentials
    /// </summary>
    public interface IEncryptionService
    {
        /// <summary>
        /// Encrypt a plaintext string using AES-GCM
        /// </summary>
        (string EncryptedBase64, string IvBase64) Encrypt(string plaintext);

        /// <summary>
        /// Decrypt an AES-GCM encrypted string
        /// </summary>
        string Decrypt(string encryptedBase64, string ivBase64);
    }
}
