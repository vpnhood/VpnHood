using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;

namespace VpnHood.AccessServer.UI
{
    public static class NavigationManagerExtensions
    {
        public static Guid? GetProjectId(this NavigationManager navigationManager)
        {
            if (TryGetQueryString<Guid>(navigationManager, "projectId", out var projectId))
                return projectId;

            var uri = QueryHelpers.AddQueryString(navigationManager.Uri, "projectId", "{0E036CD5-30B6-48FB-9F34-59BFD4623F4D}");
            navigationManager.NavigateTo(uri);
            throw new Exception("ffff");
            return null;
        }

        public static T GetQueryString<T>(this NavigationManager navigationManager, string key)
        {
            if (TryGetQueryString<T>(navigationManager, key, out var value))
                return value;
            throw new KeyNotFoundException($"{nameof(key)} does not exist in query string!");
        }

        public static bool TryGetQueryString<T>(this NavigationManager navigationManager, string key, [NotNullWhen(true)] out T? value)
        {
            var uri = navigationManager.ToAbsoluteUri(navigationManager.Uri);
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