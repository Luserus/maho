using System;

namespace Maho.Resolution;

/// <summary>
/// Internal template placeholder marking a <c>derived BaseType</c> generic argument position
/// so subclasses can re-bind that position to their own type.
/// </summary>
internal sealed class DerivedSelfSemanticType : SemanticType
{
    /// <summary> Canonical handle of the declaring base type where <c>derived</c> appeared. </summary>
    public SymbolHandle BaseTypeHandle { get; }

    public DerivedSelfSemanticType(SymbolHandle baseTypeHandle) => BaseTypeHandle = baseTypeHandle;

    public override SemanticTypeKind Kind => SemanticTypeKind.DerivedSelf;
    public override bool IsResolved => true;
    public override bool IsError => false;

    public override bool Equals(SemanticType? other) =>
        other is DerivedSelfSemanticType derived && derived.BaseTypeHandle == BaseTypeHandle;

    public override int GetHashCode() => HashCode.Combine(Kind, BaseTypeHandle);

    public override string ToString() => $"derived {BaseTypeHandle}";

    public override string ToDisplayString(ResolutionContext? context = null)
    {
        if (context is null)
            return ToString();

        var name = context.GetSymbol(BaseTypeHandle)?.Name.Text ?? BaseTypeHandle.ToString();
        return $"derived {name}";
    }
}

