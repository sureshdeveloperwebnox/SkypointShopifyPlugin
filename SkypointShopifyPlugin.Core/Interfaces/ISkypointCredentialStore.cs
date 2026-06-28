namespace SkypointShopifyPlugin.Core.Interfaces
{
    /// <summary>
    /// Persists Skypoint credentials keyed by shop domain.
    /// Credentials are written when a merchant logs in via the dashboard and
    /// reloaded automatically on server restart — no hardcoded values needed.
    /// </summary>
    public interface ISkypointCredentialStore
    {
        /// <summary>Save (or overwrite) credentials for a shop. Persists immediately.</summary>
        void Save(string shopDomain, string username, string password);

        /// <summary>Returns credentials for the shop, or null if never saved.</summary>
        (string Username, string Password)? Get(string shopDomain);

        /// <summary>Returns all shops that have stored credentials.</summary>
        IReadOnlyList<string> GetAllShops();

        /// <summary>Remove credentials for a shop.</summary>
        void Remove(string shopDomain);
    }
}
