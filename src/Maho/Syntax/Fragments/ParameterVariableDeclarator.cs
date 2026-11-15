using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Parameter declarator containing modifiers, type, and identifier. </summary>
internal sealed class ParameterVariableDeclarator : SyntaxNode
{
    /// <summary> Parameter modifiers. </summary>
    public IReadOnlyList<Token> Modifiers { get; }
    /// <summary> Declared parameter type. </summary>
    public TypeSyntax Type { get; }
    /// <summary> Parameter identifier. </summary>
    public NamedSyntax Identifier { get; }
    /// <summary> Explicit receiver passing mode, or <see cref="ReceiverKind.None"/> if normal parameter. </summary>
    public ReceiverKind ReceiverKind { get; }

    /// <summary> Indicates whether this parameter is an explicit instance receiver (<c>this</c>). </summary>
    public bool IsReceiver => ReceiverKind != ReceiverKind.None;

    /// <summary> Creates one parameter variable declarator node. </summary>
    public ParameterVariableDeclarator(IReadOnlyList<Token> modifiers, TypeSyntax type, NamedSyntax identifier, ReceiverKind receiverKind = ReceiverKind.None)
    {
        Modifiers = modifiers;
        Type = type;
        Identifier = identifier;
        ReceiverKind = receiverKind;
    }
}