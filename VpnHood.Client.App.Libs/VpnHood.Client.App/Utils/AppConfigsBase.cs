using VpnHood.Common.Utils;

namespace VpnHood.Client.App.Utils;

public class AppConfigsBase<T> : Singleton<T> where T : Singleton<T>
{
    protected void Merge(string configName)
    {
        var json = VhUtil.GetAssemblyMetadata(typeof(T).Assembly, configName, "");
        if (!string.IsNullOrEmpty(json))
            JsonSerializerExt.PopulateObject(this, json);
    }
}