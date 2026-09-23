namespace Maho.Syntax;

/// <summary> Local declaration wrapper around a macro declaration within a block or statement body. </summary>
internal sealed class LocalMacroDeclaration : LocalDeclaration
{
    /// <summary> The wrapped macro declaration. </summary>
    public MacroDeclaration Macro { get; }

    public LocalMacroDeclaration(MacroDeclaration macro) => Macro = macro;
}
