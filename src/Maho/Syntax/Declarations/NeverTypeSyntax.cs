namespace Maho.Syntax;

/// <summary>
/// Type syntax representing the '!' bottom / never type.
/// </summary>
internal sealed class NeverTypeSyntax : TypeSyntax
{
    /// <summary>
    /// The '!' exclamation mark token.
    /// </summary>
    public Token ExclamationMark { get; }

    /// <summary>
    /// Creates a new <see cref="NeverTypeSyntax"/> node.
    /// </summary>
    public NeverTypeSyntax(Token exclamationMark) => ExclamationMark = exclamationMark;
}
