namespace Maho.Syntax;

/// <summary>
/// Local declaration wrapper for a literal macro.
/// </summary>
internal sealed class LocalLiteralMacroDeclaration : LocalDeclaration
{
    public LiteralMacroDeclaration LiteralMacro { get; }

    public LocalLiteralMacroDeclaration(LiteralMacroDeclaration literalMacro)
    {
        LiteralMacro = literalMacro;
    }
}

