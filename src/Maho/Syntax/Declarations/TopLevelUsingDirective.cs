namespace Maho.Syntax;

/// <summary> Top-level construct wrapping a using directive within a compilation unit or top-level block. </summary>
internal sealed class TopLevelUsingDirective : TopLevel
{
    /// <summary> The wrapped using directive. </summary>
    public UsingDirective Directive { get; }

    /// <summary> Creates one top-level using directive node. </summary>
    public TopLevelUsingDirective(UsingDirective directive) => Directive = directive;
}
