namespace Maho.Syntax;

/// <summary> Member-level construct wrapping an <c>extern using</c> directive inside a type body. </summary>
internal sealed class MemberExternUsingDirective : Member
{
    public ExternUsingDirective Directive { get; }

    public MemberExternUsingDirective(ExternUsingDirective directive) => Directive = directive;
}

