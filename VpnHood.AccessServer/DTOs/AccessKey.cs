namespace VpnHood.AccessServer.Controllers.DTOs
{
    public class AccessKey
    {
        public string Key { get; set; }

        public AccessKey(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new System.ArgumentException($"'{nameof(key)}' cannot be null or empty.", nameof(key));
            Key = key;
        }
    }
}
