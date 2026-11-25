namespace Maho.Syntax;

/// <summary> Top-level construct wrapping an <c>extern using</c> directive. </summary>
internal sealed class TopLevelExternUsingDirective : TopLevel
{
    public ExternUsingDirective Directive { get; }

    public TopLevelExternUsingDirective(ExternUsingDirective directive) => Directive = directive;
}

