namespace Maho.Syntax;

/// <summary> Local-level construct wrapping an <c>extern using</c> directive inside a block or function body. </summary>
internal sealed class LocalExternUsingDirective : Local
{
    public ExternUsingDirective Directive { get; }

    public LocalExternUsingDirective(ExternUsingDirective directive) => Directive = directive;
}

