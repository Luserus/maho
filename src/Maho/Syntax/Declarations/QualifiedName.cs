namespace Maho.Syntax;

internal sealed class QualifiedName : NamedSyntax
{
    public SeparatedSyntaxList<NamedSyntax> Parts { get; }

    public QualifiedName(SeparatedSyntaxList<NamedSyntax> parts) => Parts = parts;
}
