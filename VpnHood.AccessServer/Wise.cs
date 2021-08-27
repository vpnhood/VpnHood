using System;

namespace VpnHood.AccessServer
{
    public class Wise<T>
    {
        public Wise(T value)
        {
            Value = value;
        }

        public T Value { get; }

        public override int GetHashCode()
        {
            return Value?.GetHashCode() ?? "".GetHashCode();
        }

        public override string ToString()
        {
            return Value?.ToString() ?? "";
        }

        public override bool Equals(object? obj)
        {
            return Equals(Value, obj);
        }

        public static implicit operator Wise<T>(T value)
        {
            return new Wise<T>(value);
        }

        public static implicit operator T(Wise<T> value)
        {
            return value != null ? value.Value : throw new NullReferenceException("Value has not been set!");
        }
    }
}