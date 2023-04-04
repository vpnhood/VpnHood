// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit
    {
    }

    public class RequiredMemberAttribute : Attribute
    {
    }

    public class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string name)
        {
            _ = name;
        }
    }
}