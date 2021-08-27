using System;

namespace VpnHood.AccessServer.DTOs
{
    public class AccessTokenKey
    {
        public AccessTokenKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException($"'{nameof(key)}' cannot be null or empty.", nameof(key));
            Key = key;
        }

        public string Key { get; set; }
    }
}