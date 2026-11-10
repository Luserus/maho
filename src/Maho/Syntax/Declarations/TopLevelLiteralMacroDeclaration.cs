namespace Maho.Syntax;

/// <summary>
/// Top-level declaration wrapper for a literal macro.
/// </summary>
internal sealed class TopLevelLiteralMacroDeclaration : TopLevelDeclaration
{
    public LiteralMacroDeclaration LiteralMacro { get; }

    public TopLevelLiteralMacroDeclaration(LiteralMacroDeclaration literalMacro)
    {
        LiteralMacro = literalMacro;
    }
}

