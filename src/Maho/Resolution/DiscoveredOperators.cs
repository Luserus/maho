using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Text;

namespace Maho.Resolution;

internal readonly record struct DiscoveredOperatorMethod(
    OperatorKind Kind,
    SymbolHandle InterfaceHandle,
    SymbolHandle MethodHandle,
    TextSpan Span,
    SourceText? Source)
{
    public string FullName => KnownIntrinsics.GetOperatorName(Kind);
}

internal sealed class DiscoveredOperators
{
    public AttributeSymbol? OperatorFunctionAttribute { get; internal set; }
    public AttributeSymbol? IntrinsicFunctionAttribute { get; internal set; }

    private readonly Dictionary<OperatorKind, List<DiscoveredOperatorMethod>> operatorsByKind = new();

    public void Register(OperatorKind kind, SymbolHandle interfaceHandle, SymbolHandle methodHandle, TextSpan span, SourceText? source)
    {
        if (!operatorsByKind.TryGetValue(kind, out var list))
        {
            list = [];
            operatorsByKind[kind] = list;
        }

        list.Add(new DiscoveredOperatorMethod(kind, interfaceHandle, methodHandle, span, source));
    }

    public void Register(string fullName, SymbolHandle interfaceHandle, SymbolHandle methodHandle, TextSpan span, SourceText? source)
    {
        if (KnownIntrinsics.TryGetOperatorKind(fullName, out var kind))
            Register(kind, interfaceHandle, methodHandle, span, source);
    }

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

    public bool TryGetOperators(string fullName, out IReadOnlyList<DiscoveredOperatorMethod> methods)
    {
        if (KnownIntrinsics.TryGetOperatorKind(fullName, out var kind))
            return TryGetOperators(kind, out methods);

        methods = [];
        return false;
    }

    public IReadOnlyList<DiscoveredOperatorMethod> GetOperators(OperatorKind kind) =>
        operatorsByKind.TryGetValue(kind, out var list) ? list : [];

    public IReadOnlyList<DiscoveredOperatorMethod> GetOperators(string fullName) =>
        KnownIntrinsics.TryGetOperatorKind(fullName, out var kind) ? GetOperators(kind) : [];

    public IEnumerable<KeyValuePair<OperatorKind, IReadOnlyList<DiscoveredOperatorMethod>>> AllByKind =>
        operatorsByKind.Select(static kv => new KeyValuePair<OperatorKind, IReadOnlyList<DiscoveredOperatorMethod>>(kv.Key, kv.Value));

    public IEnumerable<KeyValuePair<string, IReadOnlyList<DiscoveredOperatorMethod>>> All =>
        operatorsByKind.Select(static kv => new KeyValuePair<string, IReadOnlyList<DiscoveredOperatorMethod>>(KnownIntrinsics.GetOperatorName(kv.Key), kv.Value));

    public DiscoveredOperators Clone()
    {
        var clone = new DiscoveredOperators
        {
            OperatorFunctionAttribute = OperatorFunctionAttribute,
            IntrinsicFunctionAttribute = IntrinsicFunctionAttribute
        };

        foreach (var (key, value) in operatorsByKind)
            clone.operatorsByKind[key] = [..value];

        return clone;
    }
}
