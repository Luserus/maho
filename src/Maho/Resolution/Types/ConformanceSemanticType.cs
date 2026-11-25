using System;

namespace Maho.Resolution;

/// <summary>
/// Represents a concrete type paired with a specific disambiguated <see cref="InterfaceConformance"/>
/// (e.g. <c>Foo as ModA::Hash</c> in generic type or function arguments).
/// </summary>
internal sealed class ConformanceSemanticType : SemanticType
{
    public override SemanticTypeKind Kind => SemanticTypeKind.Conformance;
    public TypeRef TargetType { get; }
    public TypeRef InterfaceType { get; }
    public InterfaceConformance Conformance { get; }

    public override bool IsResolved => TargetType.IsResolved && InterfaceType.IsResolved;
    public override bool IsError => TargetType.IsError || InterfaceType.IsError;

    public ConformanceSemanticType(TypeRef targetType, InterfaceConformance conformance)
    {
        TargetType = targetType;
        InterfaceType = conformance.InterfaceType;
        Conformance = conformance;
    }

    public override bool Equals(SemanticType? other) =>
        other is ConformanceSemanticType conf &&
        Conformance.ModuleProjectId == conf.Conformance.ModuleProjectId &&
        Conformance.SerialId == conf.Conformance.SerialId &&
        TargetType.Equals(conf.TargetType) &&
        InterfaceType.Equals(conf.InterfaceType);

    public override int GetHashCode() =>
        HashCode.Combine(Kind, Conformance.ModuleProjectId, Conformance.SerialId, TargetType, InterfaceType);

    public override string ToString() =>
        $"{TargetType} as {Conformance.ModuleName}::{InterfaceType}";

    public override string ToDisplayString(ResolutionContext? context = null) =>
        $"{TargetType.ToDisplayString(context)} as {Conformance.ModuleName}::{InterfaceType.ToDisplayString(context)}";
}

