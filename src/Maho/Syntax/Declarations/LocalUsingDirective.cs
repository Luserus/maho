namespace Maho.Syntax;

/// <summary> Local construct wrapping a using directive within a function or statement block. </summary>
internal sealed class LocalUsingDirective : Local
{
    /// <summary> The wrapped using directive. </summary>
    public UsingDirective Directive { get; }

    /// <summary> Creates one local using directive node. </summary>
    public LocalUsingDirective(UsingDirective directive) => Directive = directive;
}
