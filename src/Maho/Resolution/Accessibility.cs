namespace Maho.Resolution;

public enum Accessibility : byte
{
    Private,
    Protected,
    Internal,
    Public
}

internal static class AccessibilityExtensions
{
    public static Accessibility FromTypeFlags(TypeFlags flags, bool isTopLevel)
    {
        if (flags.HasFlag(TypeFlags.Public))
            return Accessibility.Public;
        if (flags.HasFlag(TypeFlags.Internal))
            return Accessibility.Internal;
        if (flags.HasFlag(TypeFlags.Private))
            return Accessibility.Private;
        if (flags.HasFlag(TypeFlags.Protected))
            return Accessibility.Protected;
        return isTopLevel ? Accessibility.Internal : Accessibility.Private;
    }

    public static Accessibility FromFunctionFlags(FunctionFlags flags, bool isTopLevel)
    {
        if (flags.HasFlag(FunctionFlags.Public))
            return Accessibility.Public;
        if (flags.HasFlag(FunctionFlags.Internal))
            return Accessibility.Internal;
        if (flags.HasFlag(FunctionFlags.Private))
            return Accessibility.Private;
        if (flags.HasFlag(FunctionFlags.Protected))
            return Accessibility.Protected;
        return isTopLevel ? Accessibility.Internal : Accessibility.Private;
    }

    public static Accessibility FromVariableFlags(VariableFlags flags, bool isTopLevel = false)
    {
        if (flags.HasFlag(VariableFlags.Public))
            return Accessibility.Public;
        if (flags.HasFlag(VariableFlags.Internal))
            return Accessibility.Internal;
        if (flags.HasFlag(VariableFlags.Private))
            return Accessibility.Private;
        if (flags.HasFlag(VariableFlags.Protected))
            return Accessibility.Protected;
        return isTopLevel ? Accessibility.Internal : Accessibility.Private;
    }
}
