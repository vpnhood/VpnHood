#pragma warning disable CS9113 // Parameter is unread.
// ReSharper disable UnusedMember.Global
// ReSharper disable RedundantTypeDeclarationBody
// ReSharper disable UnusedParameter.Local
// ReSharper disable CheckNamespace
namespace System.Runtime.CompilerServices;

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
    }
}