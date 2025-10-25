using System.Collections.Generic;

namespace Maho.Syntax;

internal readonly struct ModifierList
{
    public List<Token> Modifiers { get; }

    public ModifierList(List<Token> modifiers) => Modifiers = modifiers;
}