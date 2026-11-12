using System;
using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary>
/// Parsed function signature, representing:
///   [premodifiers] func identifier&lt;TParams&gt;(params) [postmodifiers] -&gt; ReturnType [clauses]
/// </summary>
internal sealed class FunctionSignature : SyntaxNode
{
    /// <summary> Modifiers appearing before the 'func' keyword. </summary>
    public IReadOnlyList<Token> PreModifiers { get; }

    /// <summary> The 'func' keyword token, or null if synthesized. </summary>
    public Token? FuncKeyword { get; }

    /// <summary> Function name syntax, including any generic parameter list. </summary>
    public NamedSyntax Identifier { get; }

    /// <summary> Opening parenthesis token. </summary>
    public Token OpenParen { get; }

    /// <summary> Ordered parameter list. </summary>
    public SeparatedSyntaxList<Parameter> Parameters { get; }

    /// <summary> Closing parenthesis token. </summary>
    public Token CloseParen { get; }

    /// <summary> Modifiers appearing after the parameter list and before '-&gt;'. </summary>
    public IReadOnlyList<Token> PostModifiers { get; }

    /// <summary> The '-&gt;' token, or null if return type was omitted. </summary>
    public Token? ArrowToken { get; }

    /// <summary> Declared or synthesized return type. </summary>
    public TypeSyntax ReturnType { get; }

    /// <summary> Type constraint clauses appearing after the return type. </summary>
    public IReadOnlyList<TypeConstraintClause> Constraints { get; }

    /// <summary> All modifiers (pre-modifiers and post-modifiers combined). </summary>
    public IReadOnlyList<Token> Modifiers { get; }

    /// <summary> Creates one function signature node with full trailing-return components. </summary>
    public FunctionSignature(
        IReadOnlyList<Token> preModifiers,
        Token? funcKeyword,
        NamedSyntax identifier,
        Token openParen,
        SeparatedSyntaxList<Parameter> parameters,
        Token closeParen,
        IReadOnlyList<Token> postModifiers,
        Token? arrowToken,
        TypeSyntax returnType,
        IReadOnlyList<TypeConstraintClause> constraints)
    {
        PreModifiers = preModifiers;
        FuncKeyword = funcKeyword;
        Identifier = identifier;
        OpenParen = openParen;
        Parameters = parameters;
        CloseParen = closeParen;
        PostModifiers = postModifiers;
        ArrowToken = arrowToken;
        ReturnType = returnType;
        Constraints = constraints;

        if (postModifiers.Count == 0)
        {
            Modifiers = preModifiers;
        }
        else
        {
            var all = new List<Token>(preModifiers.Count + postModifiers.Count);
            all.AddRange(preModifiers);
            all.AddRange(postModifiers);
            Modifiers = all;
        }
    }

    /// <summary> Creates one function signature node (legacy or constructor compatibility overload). </summary>
    public FunctionSignature(
        IReadOnlyList<Token> modifiers,
        TypeSyntax returnType,
        NamedSyntax identifier,
        Token openParen,
        SeparatedSyntaxList<Parameter> parameters,
        Token closeParen,
        IReadOnlyList<TypeConstraintClause> constraints)
        : this(modifiers, null, identifier, openParen, parameters, closeParen, Array.Empty<Token>(), null, returnType, constraints)
    {
    }
}
