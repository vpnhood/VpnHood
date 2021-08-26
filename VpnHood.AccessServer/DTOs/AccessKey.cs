using System;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessKey
    {
        public string Key { get; set; }

        public AccessKey(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException($"'{nameof(key)}' cannot be null or empty.", nameof(key));
            Key = key;
        }
    }
}
