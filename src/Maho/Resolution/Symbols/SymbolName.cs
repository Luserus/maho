using System.Collections.Generic;
using System.Linq;

namespace Maho.Resolution;

internal readonly struct SymbolName
{
    private readonly SymbolPart[] parts;

    public int Count => parts.Length;

    public bool IsQualified => Count > 1;

    public SymbolPart this[int index] => parts[index];

    public SymbolPart First => parts[0];

    public SymbolPart Last => parts[^1];

    public IReadOnlyList<SymbolPart> Parts => parts;

    public SymbolName(SymbolPart[] parts) => this.parts = parts;

    public SymbolName(SymbolPart part) : this([part]) { }

    public string ToDisplayString() => string.Join('.', parts.Select(p => p.ToDisplayString()));

    public override string ToString() => ToDisplayString();
}