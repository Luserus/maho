namespace Maho.Syntax;

/// <summary> Top-level wrapper around a macro declaration. </summary>
internal sealed class TopLevelMacroDeclaration : TopLevelDeclaration
{
    /// <summary> The wrapped macro declaration. </summary>
    public MacroDeclaration Macro { get; }

    public TopLevelMacroDeclaration(MacroDeclaration macro) => Macro = macro;
}
