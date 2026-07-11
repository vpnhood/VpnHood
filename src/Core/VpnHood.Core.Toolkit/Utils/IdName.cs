namespace VpnHood.Core.Toolkit.Utils;

public class IdName<T>(T id, string name)
{
    public T Id { get; set; } = id;
    public string Name { get; set; } = name;
}

public class IdName
{
    public static IdName<T> Create<T>(T id, string name)
    {
        return new IdName<T>(id, name);
    }
}