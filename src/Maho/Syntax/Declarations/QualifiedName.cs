namespace Maho.Syntax;

/// <summary> Name syntax composed from multiple dot-separated parts. </summary>
internal sealed class QualifiedName : NamedSyntax
{
    /// <summary> Name parts in source order. </summary>
    public SeparatedSyntaxList<NamedSyntax> Parts { get; }

    /// <summary> Creates one qualified name node. </summary>
    public QualifiedName(SeparatedSyntaxList<NamedSyntax> parts) => Parts = parts;
}
