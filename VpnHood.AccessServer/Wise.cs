using System;

namespace VpnHood.AccessServer
{
    public class Wise<T>
    {
        public Wise(T value) => Value = value;
        public T Value { get; }
        public override int GetHashCode() => Value?.GetHashCode() ?? "".GetHashCode();
        public override string ToString() => Value?.ToString() ?? "";
        public override bool Equals(object? obj) => Equals(Value, obj);
        public static implicit operator Wise<T>(T value) => new(value);
        public static implicit operator T(Wise<T> value) => value != null ? value.Value : throw new NullReferenceException("Value has not been set!");
    }
}
