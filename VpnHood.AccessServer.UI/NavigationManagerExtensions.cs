
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace VpnHood.AccessServer.UI
{
    public static class NavigationManagerExtensions
    {
        public static T GetQueryString<T>(this NavigationManager navManager, string key)
        {
            if (TryGetQueryString<T>(navManager, key, out var value))
                return value;
            throw new KeyNotFoundException($"{nameof(key)} does not exist in query string!");
        }

        public static bool TryGetQueryString<T>(this NavigationManager navManager, string key, [NotNullWhen(true)] out T? value)
        {
            var uri = navManager.ToAbsoluteUri(navManager.Uri);
            value = default;

            if (!QueryHelpers.ParseQuery(uri.Query).TryGetValue(key, out var valueFromQueryString))
                return false;

            if (typeof(T) == typeof(int) && int.TryParse(valueFromQueryString, out var valueAsInt))
            {
                value = (T)(object)valueAsInt;
                return true;
            }

            if (typeof(T) == typeof(string))
            {
                value = (T)(object)valueFromQueryString;
                return true;
            }

            if (typeof(T) == typeof(Guid))
            {
                value = (T)(object)Guid.Parse(valueFromQueryString);
                return true;
            }

            if (typeof(T) == typeof(decimal) && decimal.TryParse(valueFromQueryString, out var valueAsDecimal))
            {
                value = (T)(object)valueAsDecimal;
                return true;
            }

            return false;
        }
    }
}