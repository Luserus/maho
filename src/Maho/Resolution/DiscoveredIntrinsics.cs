using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Text;

namespace Maho.Resolution;

internal sealed class DiscoveredIntrinsics
{
    public AttributeSymbol? IntrinsicTypeAttribute { get; internal set; }

    private readonly Dictionary<string, (SymbolHandle Handle, SourceText? Source, TextSpan Span)> intrinsics =
        new(StringComparer.Ordinal);

    public bool TryGet(string name, out SymbolHandle handle)
    {
        if (intrinsics.TryGetValue(name, out var entry))
        {
            handle = entry.Handle;
            return true;
        }

        handle = default;
        return false;
    }

    public bool Contains(string name) => intrinsics.ContainsKey(name);

    public bool TryGetDeclarationInfo(string name, out TextSpan span, out SourceText? source)
    {
        if (intrinsics.TryGetValue(name, out var entry))
        {
            span = entry.Span;
            source = entry.Source;
            return true;
        }

        span = default;
        source = null;
        return false;
    }

    public void Register(string name, SymbolHandle handle, TextSpan span, SourceText? source)
    {
        intrinsics[name] = (handle, source, span);
    }

    public IEnumerable<KeyValuePair<string, SymbolHandle>> All =>
        intrinsics.Select(static kv => new KeyValuePair<string, SymbolHandle>(kv.Key, kv.Value.Handle));

    public DiscoveredIntrinsics Clone()
    {
        var clone = new DiscoveredIntrinsics
        {
            IntrinsicTypeAttribute = IntrinsicTypeAttribute
        };

        foreach (var (key, value) in intrinsics)
            clone.intrinsics[key] = value;

        return clone;
    }
}

