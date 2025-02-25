using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Utils;

public class AppConfigsBase<T> : Singleton<T> where T : Singleton<T>
{
    protected void Merge(string configName)
    {
        var json = VhUtils.GetAssemblyMetadata(typeof(T).Assembly, configName, "");
        if (!string.IsNullOrEmpty(json))
            JsonSerializerExt.PopulateObject(this, json, typeof(T));
    }
}