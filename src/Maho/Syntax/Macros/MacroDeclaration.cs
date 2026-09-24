using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Declaration node for a macro definition ('macro $Name { ... }'). </summary>
internal sealed class MacroDeclaration : SyntaxNode
{
    /// <summary> Attributes applied to the macro. </summary>
    public IReadOnlyList<AttributeListSyntax> Attributes { get; }

    /// <summary> Modifiers applied to the macro (e.g. public). </summary>
    public IReadOnlyList<Token> Modifiers { get; }

    /// <summary> The 'macro' keyword token. </summary>
    public Token MacroKeyword { get; }

    /// <summary> The '$' symbol token prefixing the macro name. </summary>
    public Token DollarToken { get; }

    /// <summary> The macro name identifier token. </summary>
    public Token Name { get; }

    /// <summary> The pattern-matching or shorthand arms of this macro. </summary>
    public IReadOnlyList<MacroArmSyntax> Arms { get; }

    public MacroDeclaration(
        IReadOnlyList<AttributeListSyntax> attributes,
        IReadOnlyList<Token> modifiers,
        Token macroKeyword,
        Token dollarToken,
        Token name,
        IReadOnlyList<MacroArmSyntax> arms)
    {
        Attributes = attributes;
        Modifiers = modifiers;
        MacroKeyword = macroKeyword;
        DollarToken = dollarToken;
        Name = name;
        Arms = arms;
    }
}
