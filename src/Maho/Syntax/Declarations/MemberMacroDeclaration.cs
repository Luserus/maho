namespace Maho.Syntax;

/// <summary> Member declaration wrapper around a macro declaration within a type body. </summary>
internal sealed class MemberMacroDeclaration : Member
{
    /// <summary> The wrapped macro declaration. </summary>
    public MacroDeclaration Macro { get; }

    public MemberMacroDeclaration(MacroDeclaration macro) => Macro = macro;
}
