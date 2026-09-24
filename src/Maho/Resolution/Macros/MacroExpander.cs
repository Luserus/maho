using System;
using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution.Macros;

/// <summary>
/// Core macro expansion engine. Evaluates macro invocations against multi-arm definitions,
/// performs pattern matching, variadic unrolling, hygienic alpha-renaming, and snippet parsing.
/// Uses zero string allocation for symbol identification and matching.
/// </summary>
internal sealed class MacroExpander
{
    private readonly DiagnosticsManager diagnostics;
    private readonly ulong recursionLimit;
    private readonly Dictionary<SymbolPart, MacroDeclaration> macroTable;
    private int hygieneCounter;

    public MacroExpander(DiagnosticsManager diagnostics, ulong recursionLimit, Dictionary<SymbolPart, MacroDeclaration> macroTable)
    {
        this.diagnostics = diagnostics;
        this.recursionLimit = recursionLimit;
        this.macroTable = macroTable;
    }

    public void RegisterMacro(MacroDeclaration macro)
    {
        macroTable[new SymbolPart(macro.Name)] = macro;
    }

    public bool HasMacro(SymbolPart name) => macroTable.ContainsKey(name);

    private readonly HashSet<MacroInvocationExpression> failedInvocations = new();

    private static TextSpan GetMacroSpan(MacroInvocationExpression invocation) =>
        TextSpan.FromBounds(invocation.DollarToken.Span.Start, invocation.Name.Span.End);

    private static TextSpan GetInvocationSpan(MacroInvocationExpression invocation) =>
        TextSpan.FromBounds(invocation.DollarToken.Span.Start, (invocation.RightParen ?? invocation.Name).Span.End);

    private static ExpansionOrigin CreateOrigin(MacroDeclaration macro, MacroInvocationExpression invocation, SourceText sourceText, ExpansionOrigin? parentOrigin)
    {
        var invSpan = GetInvocationSpan(invocation);
        var invSource = invocation.DollarToken.Source ?? sourceText;
        var defSpan = TextSpan.FromBounds(macro.MacroKeyword.Span.Start, macro.Name.Span.End);
        var defSource = macro.Name.Source ?? macro.GetSource();
        return new ExpansionOrigin(invocation.Name, invSpan, invSource, defSpan, defSource, parentOrigin);
    }

    private static void AttachOrigin(SyntaxNode? node, ExpansionOrigin origin)
    {
        if (node is null)
            return;

        node.ExpansionOrigin = origin;
        foreach ((_, SyntaxNode child) in SyntaxSpan.GetChildren(node))
        {
            AttachOrigin(child, origin);
        }
    }

    public MacroDeclaration? LookupMacro(SymbolPart name) =>
        macroTable.TryGetValue(name, out var macro) ? macro : null;

    /// <summary>
    /// Expands a macro invocation in an expression context.
    /// </summary>
    public Expression ExpandExpression(MacroInvocationExpression invocation, ulong currentDepth, ExpansionOrigin? parentOrigin, SourceText sourceText)
    {
        if (failedInvocations.Contains(invocation))
            return invocation;

        var name = new SymbolPart(invocation.Name);
        if (!macroTable.TryGetValue(name, out var macro))
        {
            failedInvocations.Add(invocation);
            diagnostics.ReportUnresolvedMacro(GetMacroSpan(invocation), name.ToString(), invocation.DollarToken.Source ?? sourceText);
            return invocation;
        }

        if (recursionLimit > 0 && currentDepth >= recursionLimit)
        {
            failedInvocations.Add(invocation);
            diagnostics.ReportMacroRecursionLimitExceeded(GetInvocationSpan(invocation), name.ToString(), recursionLimit, invocation.DollarToken.Source ?? sourceText);
            return invocation;
        }

        var (arm, substitutedTokens) = MatchAndSubstitute(macro, invocation, sourceText);

        if (arm is null || substitutedTokens is null)
        {
            failedInvocations.Add(invocation);
            return invocation;
        }

        var parser = new Parser(sourceText, diagnostics);

        if (!arm.Template.IsExpression)
        {
            var blockExpr = parser.ParseBlockExpressionSnippet(substitutedTokens);
            if (blockExpr.FinalExpression is null)
            {
                // Block macro with statements cannot be used in expression context
                failedInvocations.Add(invocation);
                diagnostics.ReportInvalidMacroContext(GetInvocationSpan(invocation), name.ToString(), "an expression", "statements", invocation.DollarToken.Source ?? sourceText);
                return invocation;
            }

            var origin = CreateOrigin(macro, invocation, sourceText, parentOrigin);
            AttachOrigin(blockExpr, origin);

            return blockExpr;
        }

        var expandedExpr = parser.ParseExpressionSnippet(substitutedTokens);
        var exprOrigin = CreateOrigin(macro, invocation, sourceText, parentOrigin);
        AttachOrigin(expandedExpr, exprOrigin);

        return expandedExpr;
    }

    /// <summary>
    /// Expands a macro invocation in a statement context, returning one or more statements.
    /// </summary>
    public List<Local> ExpandStatement(MacroInvocationExpression invocation, ulong currentDepth, ExpansionOrigin? parentOrigin, SourceText sourceText)
    {
        if (failedInvocations.Contains(invocation))
            return [new LocalMacroInvocationDeclaration(invocation, null)];

        var name = new SymbolPart(invocation.Name);
        if (!macroTable.TryGetValue(name, out var macro))
        {
            failedInvocations.Add(invocation);
            diagnostics.ReportUnresolvedMacro(GetMacroSpan(invocation), name.ToString(), invocation.DollarToken.Source ?? sourceText);
            return [new LocalMacroInvocationDeclaration(invocation, null)];
        }

        if (recursionLimit > 0 && currentDepth >= recursionLimit)
        {
            failedInvocations.Add(invocation);
            diagnostics.ReportMacroRecursionLimitExceeded(GetInvocationSpan(invocation), name.ToString(), recursionLimit, invocation.DollarToken.Source ?? sourceText);
            return [new LocalMacroInvocationDeclaration(invocation, null)];
        }

        var (arm, substitutedTokens) = MatchAndSubstitute(macro, invocation, sourceText);

        if (arm is null || substitutedTokens is null)
        {
            failedInvocations.Add(invocation);
            return [new LocalMacroInvocationDeclaration(invocation, null)];
        }

        var parser = new Parser(sourceText, diagnostics);
        List<Local> statements;

        if (arm.Template.IsExpression)
        {
            var expr = parser.ParseExpressionSnippet(substitutedTokens);
            var origin = CreateOrigin(macro, invocation, sourceText, parentOrigin);
            AttachOrigin(expr, origin);
            var stmt = new LocalExpressionStatement(expr, new Token(sourceText, default, TokenKind.Semicolon, [], []));
            stmt.ExpansionOrigin = origin;
            statements = [stmt];
        }
        else
        {
            statements = parser.ParseStatementsSnippet(substitutedTokens);
            var origin = CreateOrigin(macro, invocation, sourceText, parentOrigin);
            foreach (var stmt in statements)
                AttachOrigin(stmt, origin);
        }

        return statements;
    }

    /// <summary>
    /// Expands a macro invocation in a member context, returning one or more members.
    /// </summary>
    public List<Member> ExpandMember(MacroInvocationExpression invocation, ulong currentDepth, ExpansionOrigin? parentOrigin, SourceText sourceText)
    {
        if (failedInvocations.Contains(invocation))
            return [new MemberMacroInvocationDeclaration(invocation, null)];

        var name = new SymbolPart(invocation.Name);
        if (!macroTable.TryGetValue(name, out var macro))
        {
            failedInvocations.Add(invocation);
            diagnostics.ReportUnresolvedMacro(GetMacroSpan(invocation), name.ToString(), invocation.DollarToken.Source ?? sourceText);
            return [new MemberMacroInvocationDeclaration(invocation, null)];
        }

        if (recursionLimit > 0 && currentDepth >= recursionLimit)
        {
            failedInvocations.Add(invocation);
            diagnostics.ReportMacroRecursionLimitExceeded(GetInvocationSpan(invocation), name.ToString(), recursionLimit, invocation.DollarToken.Source ?? sourceText);
            return [new MemberMacroInvocationDeclaration(invocation, null)];
        }

        var (arm, substitutedTokens) = MatchAndSubstitute(macro, invocation, sourceText);
        if (arm is null || substitutedTokens is null)
        {
            failedInvocations.Add(invocation);
            return [new MemberMacroInvocationDeclaration(invocation, null)];
        }

        var parser = new Parser(sourceText, diagnostics);
        var members = parser.ParseMembersSnippet(substitutedTokens);
        var origin = CreateOrigin(macro, invocation, sourceText, parentOrigin);

        foreach (var member in members)
            AttachOrigin(member, origin);

        return members;
    }

    /// <summary>
    /// Expands a macro invocation in a top-level context, returning one or more top-level declarations.
    /// </summary>
    public List<TopLevel> ExpandTopLevel(MacroInvocationExpression invocation, ulong currentDepth, ExpansionOrigin? parentOrigin, SourceText sourceText)
    {
        if (failedInvocations.Contains(invocation))
            return [new TopLevelMacroInvocationDeclaration(invocation, null)];

        var name = new SymbolPart(invocation.Name);
        if (!macroTable.TryGetValue(name, out var macro))
        {
            failedInvocations.Add(invocation);
            diagnostics.ReportUnresolvedMacro(GetMacroSpan(invocation), name.ToString(), invocation.DollarToken.Source ?? sourceText);
            return [new TopLevelMacroInvocationDeclaration(invocation, null)];
        }

        if (recursionLimit > 0 && currentDepth >= recursionLimit)
        {
            failedInvocations.Add(invocation);
            diagnostics.ReportMacroRecursionLimitExceeded(GetInvocationSpan(invocation), name.ToString(), recursionLimit, invocation.DollarToken.Source ?? sourceText);
            return [new TopLevelMacroInvocationDeclaration(invocation, null)];
        }

        var (arm, substitutedTokens) = MatchAndSubstitute(macro, invocation, sourceText);
        if (arm is null || substitutedTokens is null)
        {
            failedInvocations.Add(invocation);
            return [new TopLevelMacroInvocationDeclaration(invocation, null)];
        }

        var parser = new Parser(sourceText, diagnostics);
        var topLevels = parser.ParseTopLevelsSnippet(substitutedTokens);
        var origin = CreateOrigin(macro, invocation, sourceText, parentOrigin);

        foreach (var topLevel in topLevels)
            AttachOrigin(topLevel, origin);

        return topLevels;
    }

    private (MacroArmSyntax? Arm, List<Token>? SubstitutedTokens) MatchAndSubstitute(
        MacroDeclaration macro,
        MacroInvocationExpression invocation,
        SourceText sourceText)
    {
        foreach (var arm in macro.Arms)
        {
            var match = TryMatchArm(arm, invocation);

            if (match is not null)
            {
                var substituted = SubstituteTokens(arm.Template.Tokens, match.Value.Bindings, match.Value.PackBindings, sourceText);
                return (arm, substituted);
            }
        }

        diagnostics.ReportNoMatchingMacroArm(
            GetInvocationSpan(invocation),
            macro.Name.Value,
            invocation.DollarToken.Source ?? sourceText);

        return (null, null);
    }

    private readonly record struct MatchResult(
        Dictionary<SymbolPart, IReadOnlyList<Token>> Bindings,
        Dictionary<SymbolPart, List<IReadOnlyList<Token>>> PackBindings);

    private static MatchResult? TryMatchArm(MacroArmSyntax arm, MacroInvocationExpression invocation)
    {
        if (arm.Pattern is null)
            // Parameterless arm
            return invocation.Arguments.Count == 0
                ? new MatchResult(new Dictionary<SymbolPart, IReadOnlyList<Token>>(), new Dictionary<SymbolPart, List<IReadOnlyList<Token>>>())
                : null;

        var parameters = arm.Pattern.Parameters;
        bool hasVariadic = parameters.Count > 0 && parameters[^1].IsVariadic;
        int fixedCount = hasVariadic ? parameters.Count - 1 : parameters.Count;

        if (!hasVariadic && invocation.Arguments.Count != fixedCount)
            return null;

        if (hasVariadic && invocation.Arguments.Count < fixedCount)
            return null;

        var bindings = new Dictionary<SymbolPart, IReadOnlyList<Token>>();
        var packBindings = new Dictionary<SymbolPart, List<IReadOnlyList<Token>>>();

        for (int i = 0; i < fixedCount; i++)
        {
            var param = parameters[i];
            var arg = invocation.Arguments[i];

            if (!MatchParameter(param, arg))
                return null;

            if (param.AtToken is not null)
                bindings[new SymbolPart(param.Name)] = arg.Tokens;
        }

        if (hasVariadic)
        {
            var variadicParam = parameters[^1];
            var pack = new List<IReadOnlyList<Token>>();

            for (int i = fixedCount; i < invocation.Arguments.Count; i++)
            {
                var arg = invocation.Arguments[i];

                if (!MatchParameter(variadicParam, arg))
                    return null;

                pack.Add(arg.Tokens);
            }

            packBindings[new SymbolPart(variadicParam.Name)] = pack;
        }

        return new MatchResult(bindings, packBindings);
    }

    private static bool MatchParameter(MacroPatternParameter param, MacroArgumentSyntax arg)
    {
        if (param.Kind is MacroParameterKind.Literal)
        {
            if (param.LiteralExpression is LiteralExpression lit && arg.Tokens.Count == 1)
                return lit.Literal.Span.Length == arg.Tokens[0].Span.Length &&
                       lit.Literal.Source.AsSpan(lit.Literal.Span).SequenceEqual(arg.Tokens[0].Source.AsSpan(arg.Tokens[0].Span));

            return false;
        }

        if (param.Kind is MacroParameterKind.Identifier)
            return arg.Tokens.Count == 1 && arg.Tokens[0].Kind is TokenKind.Identifier;

        if (param.Kind is MacroParameterKind.SingleToken)
            return arg.Tokens.Count == 1;

        if (param.Kind is MacroParameterKind.TokenStream)
            return arg.Tokens.Count > 0;

        return arg.Tokens.Count > 0;
    }

    private List<Token> SubstituteTokens(IReadOnlyList<Token> templateTokens, Dictionary<SymbolPart, IReadOnlyList<Token>> bindings,
        Dictionary<SymbolPart, List<IReadOnlyList<Token>>> packBindings, SourceText sourceText)
    {
        var result = new List<Token>();
        int i = 0;

        while (i < templateTokens.Count)
        {
            // 1. Check for repetition unrolling: $( ... @pack ... )...
            if (templateTokens[i].Kind is TokenKind.Dollar &&
                i + 1 < templateTokens.Count &&
                templateTokens[i + 1].Kind is TokenKind.LeftParen)
            {
                int closeParenIdx = FindMatchingParen(templateTokens, i + 1);
                if (closeParenIdx > 0 && IsEllipsisAt(templateTokens, closeParenIdx + 1, out int ellipsisLength))
                {
                    var repetitionBody = SliceTokens(templateTokens, i + 2, closeParenIdx - (i + 2));
                    var referencedPack = FindReferencedPack(repetitionBody, packBindings);

                    if (referencedPack.HasValue && packBindings.TryGetValue(referencedPack.Value, out var pack))
                    {
                        for (int elemIdx = 0; elemIdx < pack.Count; elemIdx++)
                        {
                            var elementBindings = new Dictionary<SymbolPart, IReadOnlyList<Token>>(bindings)
                            {
                                [referencedPack.Value] = pack[elemIdx]
                            };
                            foreach (var tokenPart in FindAllPackReferences(repetitionBody, referencedPack.Value))
                            {
                                elementBindings[tokenPart] = pack[elemIdx];
                            }
                            var unrolled = SubstituteTokens(repetitionBody, elementBindings, packBindings, sourceText);
                            result.AddRange(unrolled);
                        }
                    }

                    i = closeParenIdx + 1 + ellipsisLength;
                    continue;
                }
            }

            // 2. Check for in-place pack spread: @pack...
            if (templateTokens[i].Kind is TokenKind.AtSymbol &&
                i + 1 < templateTokens.Count &&
                templateTokens[i + 1].Kind is TokenKind.Identifier &&
                IsEllipsisAt(templateTokens, i + 2, out int spreadEllipsisLen))
            {
                var paramPart = new SymbolPart(templateTokens[i + 1]);

                if (packBindings.TryGetValue(paramPart, out var pack))
                {
                    for (int elemIdx = 0; elemIdx < pack.Count; elemIdx++)
                    {
                        if (elemIdx > 0)
                            result.Add(new Token(sourceText, default, TokenKind.Comma, [], []));

                        result.AddRange(pack[elemIdx]);
                    }

                    i += 2 + spreadEllipsisLen;
                    continue;
                }
            }

            // 2.5 Check for nameof(@param) folding
            if ((templateTokens[i].MatchingKind is MatchingKeywordKind.Nameof || templateTokens[i].Value == "nameof") &&
                i + 1 < templateTokens.Count &&
                templateTokens[i + 1].Kind is TokenKind.LeftParen)
            {
                int closeParenIdx = FindMatchingParen(templateTokens, i + 1);
                if (closeParenIdx == i + 4 &&
                    templateTokens[i + 2].Kind is TokenKind.AtSymbol &&
                    templateTokens[i + 3].Kind is TokenKind.Identifier)
                {
                    var paramPart = new SymbolPart(templateTokens[i + 3]);
                    if (bindings.TryGetValue(paramPart, out var argTokens) && argTokens.Count > 0)
                    {
                        var targetToken = argTokens[^1];
                        string targetName = targetToken.Value;
                        string quoted = $"\"{targetName}\"";
                        var stringToken = new Token(
                            new SourceText(quoted),
                            new TextSpan(0, quoted.Length),
                            TokenKind.String,
                            templateTokens[i].LeadingTrivia,
                            templateTokens[closeParenIdx].TrailingTrivia);

                        result.Add(stringToken);
                        i = closeParenIdx + 1;
                        continue;
                    }
                }
            }

            // 3. Check for scalar parameter substitution: @param
            if (templateTokens[i].Kind is TokenKind.AtSymbol && i + 1 < templateTokens.Count && templateTokens[i + 1].Kind is TokenKind.Identifier)
            {
                var paramPart = new SymbolPart(templateTokens[i + 1]);

                if (bindings.TryGetValue(paramPart, out var argTokens))
                {
                    result.AddRange(argTokens);
                    i += 2;
                    continue;
                }
            }

            // Normal token
            result.Add(templateTokens[i]);
            i++;
        }

        // Apply ## token concatenation
        result = ProcessTokenConcatenation(result, sourceText);

        // Apply hygiene alpha-renaming
        ApplyHygiene(result, bindings, packBindings, sourceText);

        return result;
    }

    private List<Token> ProcessTokenConcatenation(List<Token> tokens, SourceText sourceText)
    {
        bool hasConcat = false;
        for (int k = 0; k < tokens.Count - 1; k++)
        {
            if (IsConcatenationOperator(tokens, k))
            {
                hasConcat = true;
                break;
            }
        }

        if (!hasConcat)
            return tokens;

        var result = new List<Token>(tokens.Count);
        int i = 0;
        while (i < tokens.Count)
        {
            if (IsConcatenationOperator(tokens, i))
            {
                var hash1 = tokens[i];
                var hash2 = tokens[i + 1];

                if (result.Count == 0)
                {
                    diagnostics.ReportInvalidTokenConcatenation(hash1.Span, "'##' cannot appear at the start of a macro template", sourceText);
                    i += 2;
                    continue;
                }

                if (i + 2 >= tokens.Count)
                {
                    diagnostics.ReportInvalidTokenConcatenation(hash2.Span, "'##' cannot appear at the end of a macro template", sourceText);
                    i += 2;
                    continue;
                }

                var left = result[^1];
                var right = tokens[i + 2];

                if (TryConcatenateTokens(left, right, out var mergedToken))
                {
                    result[^1] = mergedToken!;
                    i += 3;
                }
                else
                {
                    diagnostics.ReportInvalidTokenConcatenation(
                        new TextSpan(left.Span.Start, Math.Max(0, right.Span.End - left.Span.Start)),
                        $"pasting '{left.Value}' and '{right.Value}' does not result in a valid token",
                        sourceText);
                    i += 3;
                }
            }
            else
            {
                result.Add(tokens[i]);
                i++;
            }
        }

        return result;
    }

    private static bool IsConcatenationOperator(IReadOnlyList<Token> tokens, int index)
    {
        if (index + 1 >= tokens.Count)
            return false;

        var t1 = tokens[index];
        var t2 = tokens[index + 1];

        return t1.Kind is TokenKind.Octothorpe &&
               t2.Kind is TokenKind.Octothorpe &&
               t1.Span.End == t2.Span.Start &&
               t1.TrailingTrivia.Length == 0 &&
               t2.LeadingTrivia.Length == 0;
    }

    private static bool TryConcatenateTokens(Token a, Token b, out Token? mergedToken)
    {
        string combined = a.Value + b.Value;
        var dummyDiagnostics = new DiagnosticsManager();
        var lexer = new Lexer(new SourceText(combined), dummyDiagnostics);
        var lexedTokens = lexer.Lex();

        if (!dummyDiagnostics.HasErrors &&
            lexedTokens.Count == 2 &&
            lexedTokens[0].Kind != TokenKind.BadToken &&
            lexedTokens[1].Kind == TokenKind.EndToken)
        {
            mergedToken = new Token(
                lexedTokens[0].Source,
                lexedTokens[0].Span,
                lexedTokens[0].Kind,
                a.LeadingTrivia,
                b.TrailingTrivia,
                lexedTokens[0].MatchingKind);
            return true;
        }

        mergedToken = null;
        return false;
    }

    private void ApplyHygiene(
        List<Token> tokens,
        Dictionary<SymbolPart, IReadOnlyList<Token>> bindings,
        Dictionary<SymbolPart, List<IReadOnlyList<Token>>> packBindings,
        SourceText sourceText)
    {
        var localNames = new HashSet<SymbolPart>();
        var renameMap = new Dictionary<SymbolPart, string>();

        for (int i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i].MatchingKind is MatchingKeywordKind.Var && tokens[i + 1].Kind is TokenKind.Identifier)
            {
                var varPart = new SymbolPart(tokens[i + 1]);
                if (!bindings.ContainsKey(varPart) && !packBindings.ContainsKey(varPart) && !localNames.Contains(varPart))
                {
                    localNames.Add(varPart);
                    renameMap[varPart] = $"{varPart}__m{++hygieneCounter}";
                }
            }
        }

        if (renameMap.Count == 0)
            return;

        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind is TokenKind.Identifier && renameMap.TryGetValue(new SymbolPart(tokens[i]), out var newName))
            {
                tokens[i] = new Token(
                    new SourceText(newName),
                    new TextSpan(0, newName.Length),
                    TokenKind.Identifier,
                    tokens[i].LeadingTrivia,
                    tokens[i].TrailingTrivia);
            }
        }
    }

    private static int FindMatchingParen(IReadOnlyList<Token> tokens, int openParenIdx)
    {
        int depth = 0;

        for (int i = openParenIdx; i < tokens.Count; i++)
        {
            if (tokens[i].Kind is TokenKind.LeftParen)
                depth++;
            else if (tokens[i].Kind is TokenKind.RightParen)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }
        return -1;
    }

    private static bool IsEllipsisAt(IReadOnlyList<Token> tokens, int index, out int length)
    {
        if (index < tokens.Count && tokens[index].Kind is TokenKind.DotDotDot)
        {
            length = 1;
            return true;
        }

        if (index + 2 < tokens.Count &&
            tokens[index].Kind is TokenKind.Dot &&
            tokens[index + 1].Kind is TokenKind.Dot &&
            tokens[index + 2].Kind is TokenKind.Dot &&
            tokens[index].Span.End == tokens[index + 1].Span.Start &&
            tokens[index].TrailingTrivia.Length == 0 &&
            tokens[index + 1].LeadingTrivia.Length == 0 &&
            tokens[index + 1].Span.End == tokens[index + 2].Span.Start &&
            tokens[index + 1].TrailingTrivia.Length == 0 &&
            tokens[index + 2].LeadingTrivia.Length == 0)
        {
            length = 3;
            return true;
        }

        length = 0;
        return false;
    }

    private static List<Token> SliceTokens(IReadOnlyList<Token> tokens, int start, int count)
    {
        var slice = new List<Token>(count);

        for (int i = 0; i < count && start + i < tokens.Count; i++)
            slice.Add(tokens[start + i]);

        return slice;
    }

    private static SymbolPart? FindReferencedPack(IReadOnlyList<Token> tokens, Dictionary<SymbolPart, List<IReadOnlyList<Token>>> packBindings)
    {
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Kind is TokenKind.AtSymbol && tokens[i + 1].Kind is TokenKind.Identifier)
            {
                var part = new SymbolPart(tokens[i + 1]);

                if (packBindings.ContainsKey(part))
                    return part;

                var span = part.AsSpan();

                foreach (var key in packBindings.Keys)
                {
                    var keySpan = key.AsSpan();

                    if (span.SequenceEqual(keySpan))
                        return key;
                    if (keySpan.Length == span.Length + 1 && keySpan.StartsWith(span) && keySpan[^1] == 's')
                        return key;
                    if (span.Length == keySpan.Length + 1 && span.StartsWith(keySpan) && span[^1] == 's')
                        return key;
                }
            }
        }

        return null;
    }

    private static List<SymbolPart> FindAllPackReferences(IReadOnlyList<Token> tokens, SymbolPart packName)
    {
        var list = new List<SymbolPart>();
        var packSpan = packName.AsSpan();

        for (int i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i].Kind is TokenKind.AtSymbol && tokens[i + 1].Kind is TokenKind.Identifier)
            {
                var part = new SymbolPart(tokens[i + 1]);
                var span = part.AsSpan();
                if (span.SequenceEqual(packSpan) ||
                    (packSpan.Length == span.Length + 1 && packSpan.StartsWith(span) && packSpan[^1] == 's') ||
                    (span.Length == packSpan.Length + 1 && span.StartsWith(packSpan) && span[^1] == 's'))
                {
                    list.Add(part);
                }
            }
        }

        return list;
    }
}
