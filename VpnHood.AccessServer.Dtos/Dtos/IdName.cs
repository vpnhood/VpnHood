namespace VpnHood.AccessServer.Dtos;

public class IdName<T>
{
    public IdName(T id, string name)
    {
        Id = id;
        Name = name;
    }



    public T Id { get; set; }
    public string Name { get; set; }
}

public class IdName
{
    public static IdName<T> Create<T>(T id, string name)
    {
        return new IdName<T>(id, name);
    }
}
