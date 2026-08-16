using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> File-level pragma directive parsed before the compilation unit's top-level members. </summary>
internal sealed class PragmaDirective : SyntaxNode
{
    /// <summary> The leading <c>#</c> token. </summary>
    public Token HashToken { get; }
    /// <summary> The <c>pragma</c> identifier. </summary>
    public Token PragmaKeyword { get; }
    /// <summary> Directive name. </summary>
    public Token Name { get; }
    /// <summary> Directive value. </summary>
    public Token Value { get; }

    /// <summary> Creates one parsed pragma directive. </summary>
    public PragmaDirective(Token hashToken, Token pragmaKeyword, Token name, Token value)
    {
        HashToken = hashToken;
        PragmaKeyword = pragmaKeyword;
        Name = name;
        Value = value;
    }

    /// <summary> Determines whether the ordered pragma list leaves executable top-level statements enabled. </summary>
    public static bool EnablesTopLevelStatements(IReadOnlyList<PragmaDirective> pragmas)
    {
        bool enabled = false;

        foreach (PragmaDirective pragma in pragmas)
        {
            if (pragma.PragmaKeyword.Value != "pragma" || pragma.Name.Value != "toplevel")
                continue;

            if (pragma.Value.Value == "enable")
                enabled = true;
            else if (pragma.Value.Value == "disable")
                enabled = false;
        }

        return enabled;
    }
}
