using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Text;

namespace Maho.Resolution;

/// <summary>
/// Represents a discovered operator method declaration found on an operator interface.
/// </summary>
/// <param name="Kind">The operator classification kind.</param>
/// <param name="InterfaceHandle">The handle of the enclosing operator interface.</param>
/// <param name="MethodHandle">The handle of the operator method itself.</param>
/// <param name="Span">Source text span of the operator declaration.</param>
/// <param name="Source">Source text where the operator is declared.</param>
internal readonly record struct DiscoveredOperatorMethod(
    OperatorKind Kind,
    SymbolHandle InterfaceHandle,
    SymbolHandle MethodHandle,
    TextSpan Span,
    SourceText? Source)
{
    /// <summary>Gets the canonical canonical name of this operator kind.</summary>
    public string FullName => KnownIntrinsics.GetOperatorName(Kind);
}

/// <summary>
/// Thread-safe registry that stores all discovered operator interface methods across the compilation.
/// Populated during <see cref="Passes.IntrinsicDiscoveryPass"/> and queried during expression resolution.
/// </summary>
internal sealed class DiscoveredOperators
{
    /// <summary>The symbol handle of the <c>[OperatorFunction]</c> attribute.</summary>
    public AttributeSymbol? OperatorFunctionAttribute { get; internal set; }

    /// <summary>The symbol handle of the <c>[IntrinsicFunction]</c> attribute.</summary>
    public AttributeSymbol? IntrinsicFunctionAttribute { get; internal set; }

    private readonly Dictionary<OperatorKind, List<DiscoveredOperatorMethod>> operatorsByKind = new();

    /// <summary>
    /// Registers a discovered operator method by its classified <see cref="OperatorKind"/>.
    /// </summary>
    public void Register(OperatorKind kind, SymbolHandle interfaceHandle, SymbolHandle methodHandle, TextSpan span, SourceText? source)
    {
        if (!operatorsByKind.TryGetValue(kind, out var list))
        {
            list = [];
            operatorsByKind[kind] = list;
        }

        list.Add(new DiscoveredOperatorMethod(kind, interfaceHandle, methodHandle, span, source));
    }

    /// <summary>
    /// Registers a discovered operator method by its string name (e.g. "Addition", "Callable").
    /// </summary>
    public void Register(string fullName, SymbolHandle interfaceHandle, SymbolHandle methodHandle, TextSpan span, SourceText? source)
    {
        if (KnownIntrinsics.TryGetOperatorKind(fullName, out var kind))
            Register(kind, interfaceHandle, methodHandle, span, source);
    }

    /// <summary>
    /// Attempts to retrieve all registered operator methods for the given <paramref name="kind"/>.
    /// </summary>
    public bool TryGetOperators(OperatorKind kind, out IReadOnlyList<DiscoveredOperatorMethod> methods)
    {
        if (operatorsByKind.TryGetValue(kind, out var list))
        {
            methods = list;
            return true;
        }

        methods = [];
        return false;
    }

    /// <summary>
    /// Attempts to retrieve all registered operator methods for the given canonical operator name.
    /// </summary>
    public bool TryGetOperators(string fullName, out IReadOnlyList<DiscoveredOperatorMethod> methods)
    {
        if (KnownIntrinsics.TryGetOperatorKind(fullName, out var kind))
            return TryGetOperators(kind, out methods);

        methods = [];
        return false;
    }

    /// <summary>Gets all registered operator methods for the specified kind, or empty list if none.</summary>
    public IReadOnlyList<DiscoveredOperatorMethod> GetOperators(OperatorKind kind) =>
        operatorsByKind.TryGetValue(kind, out var list) ? list : [];

    /// <summary>Gets all registered operator methods for the specified name, or empty list if none.</summary>
    public IReadOnlyList<DiscoveredOperatorMethod> GetOperators(string fullName) =>
        KnownIntrinsics.TryGetOperatorKind(fullName, out var kind) ? GetOperators(kind) : [];

    /// <summary>Enumerates all registered operators grouped by kind.</summary>
    public IEnumerable<KeyValuePair<OperatorKind, IReadOnlyList<DiscoveredOperatorMethod>>> AllByKind =>
        operatorsByKind.Select(static kv => new KeyValuePair<OperatorKind, IReadOnlyList<DiscoveredOperatorMethod>>(kv.Key, kv.Value));

    /// <summary>Enumerates all registered operators grouped by canonical name.</summary>
    public IEnumerable<KeyValuePair<string, IReadOnlyList<DiscoveredOperatorMethod>>> All =>
        operatorsByKind.Select(static kv => new KeyValuePair<string, IReadOnlyList<DiscoveredOperatorMethod>>(KnownIntrinsics.GetOperatorName(kv.Key), kv.Value));

    /// <summary>Creates a deep copy of this operator registry for isolated downstream context branching.</summary>
    public DiscoveredOperators Clone()
    {
        var clone = new DiscoveredOperators
        {
            OperatorFunctionAttribute = OperatorFunctionAttribute,
            IntrinsicFunctionAttribute = IntrinsicFunctionAttribute
        };

        foreach (var (key, value) in operatorsByKind)
            clone.operatorsByKind[key] = [.. value];

        return clone;
    }
}

