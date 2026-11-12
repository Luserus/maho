namespace Maho.Syntax;

/// <summary>
/// Member declaration wrapper for a literal macro.
/// </summary>
internal sealed class MemberLiteralMacroDeclaration : Member
{
    public LiteralMacroDeclaration LiteralMacro { get; }

    public MemberLiteralMacroDeclaration(LiteralMacroDeclaration literalMacro)
    {
        LiteralMacro = literalMacro;
    }
}

