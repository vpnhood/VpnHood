namespace VpnHood.AccessServer.Authorization;

public class CustomEnum<T> where T : struct
{
    public T Id { get; }
    public string Name { get; }

    public CustomEnum(T id, string name)
    {
        Id = id;
        Name = name;
    }

    public static implicit operator T(CustomEnum<T> i) => i.Id;

    public static bool operator ==(CustomEnum<T> left, CustomEnum<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CustomEnum<T> left, CustomEnum<T> right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        return Name;
    }

    public bool Equals(CustomEnum<T> other)
    {
        return Id.Equals(other.Id);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null)
            return false;

        return obj is CustomEnum<T> eventId && Equals(eventId);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}