namespace Maho.Syntax;

/// <summary> Member declaration wrapping a using directive within a type or member block. </summary>
internal sealed class MemberUsingDirective : Member
{
    /// <summary> The wrapped using directive. </summary>
    public UsingDirective Directive { get; }

    /// <summary> Creates one member using directive node. </summary>
    public MemberUsingDirective(UsingDirective directive) => Directive = directive;
}
