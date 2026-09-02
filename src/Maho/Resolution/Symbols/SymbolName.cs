namespace Maho.Resolution;

internal readonly struct SymbolName
{
    private readonly SymbolPart[] parts;

    public int Count => parts.Length;

    public bool IsQualified => Count > 1;

    public SymbolPart this[int index] => parts[index];

    public SymbolPart First => parts[0];

    public SymbolPart Last => parts[^1];

    public SymbolName(SymbolPart[] parts) => this.parts = parts;

    public SymbolName(SymbolPart part) : this([part]) { }
}
