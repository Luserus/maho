using System;
using System.Collections.Generic;
using Maho.Diagnostics;
using Maho.Resolution.Macros;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

/// <summary>
/// Resolution pass that expands all macro invocations across the syntax tree,
/// resolving mixins, statement unrolling, and recursive pattern matching.
/// Operates on SymbolPart identifiers with zero string allocations during expansion.
/// </summary>
internal sealed class MacroExpansionPass : ResolutionPass
{
    public override void Resolve(ResolutionContext context)
    {
        var macroTable = new Dictionary<SymbolPart, MacroDeclaration>();

        // 1. Collect all declared macros from symbol store
        foreach (var macroSymbol in context.MacroSymbols)
            if (macroSymbol.Syntax is not null)
                macroTable[macroSymbol.Name] = macroSymbol.Syntax;

        // Also collect directly from syntax tree roots if not yet discovered
        foreach (var root in context.SyntaxTree.Roots)
            CollectMacros(root.Members, macroTable);

        ulong recursionLimit = context.Options?.MacroRecursionLimit ?? 128;
        var expander = new MacroExpander(context.Diagnostics, recursionLimit, macroTable);

        ulong currentDepth = 0;
        bool anyExpanded;

        do
        {
            anyExpanded = false;

            if (recursionLimit > 0 && currentDepth > recursionLimit)
                break;

            foreach (var root in context.SyntaxTree.Roots)
            {
                var sourceText = root.GetSource() ?? (root.Members.Count > 0 ? root.Members[0].GetSource() : null) ?? new SourceText(string.Empty);

                if (ExpandCompilationUnit(root, expander, currentDepth, sourceText))
                    anyExpanded = true;
            }

            if (anyExpanded)
                currentDepth++;

        } while (anyExpanded);
    }

    private static void CollectMacros(IEnumerable<TopLevel> members, Dictionary<SymbolPart, MacroDeclaration> macroTable)
    {
        foreach (var member in members)
        {
            if (member is TopLevelMacroDeclaration macroDecl)
                macroTable[new SymbolPart(macroDecl.Macro.Name)] = macroDecl.Macro;
            else if (member is NamespaceDeclaration ns && ns.Body is NamespaceBlockBody nsBody)
                CollectMacros(nsBody.Members, macroTable);
            else if (member is TopLevelTypeDeclaration typeDecl && typeDecl.Type.Body is TypeBlockBody body)
                CollectMemberMacros(body.Members, macroTable);
            else if (member is TopLevelFunctionDeclaration funcDecl && funcDecl.Function.Body is FunctionBlockBody funcBody)
                CollectLocalMacros(funcBody.Locals, macroTable);
        }
    }

    private static void CollectMemberMacros(IEnumerable<Member> members, Dictionary<SymbolPart, MacroDeclaration> macroTable)
    {
        foreach (var member in members)
        {
            if (member is MemberMacroDeclaration innerMacro)
                macroTable[new SymbolPart(innerMacro.Macro.Name)] = innerMacro.Macro;
            else if (member is MemberTypeDeclaration nestedType && nestedType.Type.Body is TypeBlockBody nestedBody)
                CollectMemberMacros(nestedBody.Members, macroTable);
            else if (member is MemberFunctionDeclaration method && method.Function.Body is FunctionBlockBody methodBody)
                CollectLocalMacros(methodBody.Locals, macroTable);
        }
    }

    private static void CollectLocalMacros(IEnumerable<Local> locals, Dictionary<SymbolPart, MacroDeclaration> macroTable)
    {
        foreach (var local in locals)
        {
            if (local is LocalMacroDeclaration localMacro)
                macroTable[new SymbolPart(localMacro.Macro.Name)] = localMacro.Macro;
            else if (local is LocalBlockStatement block)
                CollectLocalMacros(block.Locals, macroTable);
            else if (local is LocalFunctionDeclaration localFunc && localFunc.Function.Body is FunctionBlockBody funcBody)
                CollectLocalMacros(funcBody.Locals, macroTable);
            else if (local is LocalTypeDeclaration localType && localType.Type.Body is TypeBlockBody typeBody)
                CollectMemberMacros(typeBody.Members, macroTable);
        }
    }

    private static bool ExpandCompilationUnit(CompilationUnit unit, MacroExpander expander, ulong currentDepth, SourceText sourceText)
    {
        if (ExpandTopLevels(unit.Members, expander, currentDepth, sourceText, out var newMembers))
        {
            unit.Members = newMembers;
            return true;
        }

        return false;
    }

    private static bool ExpandTopLevels(IReadOnlyList<TopLevel> members, MacroExpander expander, ulong currentDepth, SourceText sourceText, out List<TopLevel> newMembers)
    {
        bool changed = false;
        newMembers = [];

        foreach (var member in members)
        {
            if (member is TopLevelMacroInvocationDeclaration inv)
            {
                var expanded = expander.ExpandTopLevel(inv.Invocation, currentDepth, inv.ExpansionOrigin, sourceText);

                if (expanded.Count == 1 && expanded[0] is TopLevelMacroInvocationDeclaration unexp && ReferenceEquals(unexp.Invocation, inv.Invocation))
                    newMembers.Add(member);
                else
                {
                    newMembers.AddRange(expanded);
                    changed = true;
                }
            }
            else if (TryExtractMacroAttributeFromTopLevel(member, sourceText, out var syntheticInvocation))
            {
                var expanded = expander.ExpandTopLevel(syntheticInvocation!, currentDepth, null, sourceText);
                if (expanded.Count == 1 && expanded[0] is TopLevelMacroInvocationDeclaration unexp && ReferenceEquals(unexp.Invocation, syntheticInvocation))
                    newMembers.Add(member);
                else
                {
                    newMembers.AddRange(expanded);
                    changed = true;
                }
            }
            else if (member is NamespaceDeclaration ns && ns.Body is NamespaceBlockBody nsBody)
            {
                if (ExpandTopLevels(nsBody.Members, expander, currentDepth, sourceText, out var expandedNsMembers))
                {
                    nsBody.Members = expandedNsMembers;
                    changed = true;
                }

                newMembers.Add(member);
            }
            else if (member is TopLevelTypeDeclaration typeDecl)
            {
                if (ExpandTypeDeclaration(typeDecl.Type, expander, currentDepth, sourceText))
                    changed = true;

                newMembers.Add(member);
            }
            else if (member is TopLevelFunctionDeclaration funcDecl)
            {
                if (ExpandFunctionBody(funcDecl.Function.Body, expander, currentDepth, sourceText))
                    changed = true;

                newMembers.Add(member);
            }
            else if (member is TopLevelBlockDeclaration block)
            {
                if (ExpandTopLevelBlock(block, expander, currentDepth, sourceText))
                    changed = true;

                newMembers.Add(member);
            }
            else if (member is TopLevelStatement stmt)
            {
                if (ExpandTopLevelStatement(stmt, expander, currentDepth, sourceText))
                    changed = true;

                newMembers.Add(member);
            }
            else
                newMembers.Add(member);
        }

        return changed;
    }

    private static bool ExpandTypeDeclaration(TypeDeclaration type, MacroExpander expander, ulong currentDepth, SourceText sourceText)
    {
        if (type.Body is not TypeBlockBody body)
            return false;

        bool changed = false;
        var newMembers = new List<Member>();

        foreach (var member in body.Members)
        {
            if (member is MemberMacroInvocationDeclaration inv)
            {
                var expanded = expander.ExpandMember(inv.Invocation, currentDepth, inv.ExpansionOrigin, sourceText);

                if (expanded.Count == 1 && expanded[0] is MemberMacroInvocationDeclaration unexp && ReferenceEquals(unexp.Invocation, inv.Invocation))
                    newMembers.Add(member);
                else
                {
                    newMembers.AddRange(expanded);
                    changed = true;
                }
            }
            else if (TryExtractMacroAttributeFromMember(member, sourceText, out var syntheticInvocation))
            {
                var expanded = expander.ExpandMember(syntheticInvocation!, currentDepth, null, sourceText);
                if (expanded.Count == 1 && expanded[0] is MemberMacroInvocationDeclaration unexp && ReferenceEquals(unexp.Invocation, syntheticInvocation))
                    newMembers.Add(member);
                else
                {
                    newMembers.AddRange(expanded);
                    changed = true;
                }
            }
            else if (member is MemberTypeDeclaration nestedType)
            {
                if (ExpandTypeDeclaration(nestedType.Type, expander, currentDepth, sourceText))
                    changed = true;

                newMembers.Add(member);
            }
            else if (member is MemberFunctionDeclaration method)
            {
                if (ExpandFunctionBody(method.Function.Body, expander, currentDepth, sourceText))
                    changed = true;

                newMembers.Add(member);
            }
            else
                newMembers.Add(member);
        }

        if (changed)
            body.Members = newMembers;

        return changed;
    }

    private static bool ExpandFunctionBody(FunctionBody body, MacroExpander expander, ulong currentDepth, SourceText sourceText)
    {
        if (body is not FunctionBlockBody blockBody)
            return false;

        bool changed = false;
        var newLocals = new List<Local>();

        foreach (var local in blockBody.Locals)
        {
            if (local is LocalMacroInvocationDeclaration localInv)
            {
                var expanded = expander.ExpandStatement(localInv.Invocation, currentDepth, localInv.ExpansionOrigin, sourceText);

                if (expanded.Count == 1 && expanded[0] is LocalMacroInvocationDeclaration unexp && ReferenceEquals(unexp.Invocation, localInv.Invocation))
                    newLocals.Add(local);
                else
                {
                    newLocals.AddRange(expanded);
                    changed = true;
                }
            }
            else if (local is LocalExpressionStatement exprStmt && exprStmt.Expression is MacroInvocationExpression inv)
            {
                var expanded = expander.ExpandStatement(inv, currentDepth, exprStmt.ExpansionOrigin, sourceText);

                if (expanded.Count == 1 && expanded[0] is LocalMacroInvocationDeclaration unexp && ReferenceEquals(unexp.Invocation, inv))
                    newLocals.Add(local);
                else
                {
                    newLocals.AddRange(expanded);
                    changed = true;
                }
            }
            else if (local is LocalBlockStatement block)
            {
                if (ExpandLocalBlock(block, expander, currentDepth, sourceText))
                    changed = true;

                newLocals.Add(local);
            }
            else
            {
                if (ExpandLocal(local, expander, currentDepth, sourceText))
                    changed = true;

                newLocals.Add(local);
            }
        }

        if (changed)
            blockBody.Locals = newLocals;

        return changed;
    }

    private static bool ExpandLocalBlock(LocalBlockStatement block, MacroExpander expander, ulong currentDepth, SourceText sourceText)
    {
        bool changed = false;
        var newLocals = new List<Local>();

        foreach (var local in block.Locals)
        {
            if (local is LocalMacroInvocationDeclaration localInv)
            {
                var expanded = expander.ExpandStatement(localInv.Invocation, currentDepth, localInv.ExpansionOrigin, sourceText);

                if (expanded.Count == 1 && expanded[0] is LocalMacroInvocationDeclaration unexp && ReferenceEquals(unexp.Invocation, localInv.Invocation))
                    newLocals.Add(local);
                else
                {
                    newLocals.AddRange(expanded);
                    changed = true;
                }
            }
            else if (local is LocalExpressionStatement exprStmt && exprStmt.Expression is MacroInvocationExpression inv)
            {
                var expanded = expander.ExpandStatement(inv, currentDepth, exprStmt.ExpansionOrigin, sourceText);

                if (expanded.Count == 1 && expanded[0] is LocalMacroInvocationDeclaration unexp && ReferenceEquals(unexp.Invocation, inv))
                    newLocals.Add(local);
                else
                {
                    newLocals.AddRange(expanded);
                    changed = true;
                }
            }
            else if (local is LocalBlockStatement innerBlock)
            {
                if (ExpandLocalBlock(innerBlock, expander, currentDepth, sourceText))
                    changed = true;

                newLocals.Add(local);
            }
            else
            {
                if (ExpandLocal(local, expander, currentDepth, sourceText))
                    changed = true;

                newLocals.Add(local);
            }
        }

        if (changed)
            block.Locals = newLocals;

        return changed;
    }

    private static bool ExpandTopLevelBlock(TopLevelBlockDeclaration block, MacroExpander expander, ulong currentDepth, SourceText sourceText)
    {
        bool changed = false;
        var newMembers = new List<TopLevel>();

        foreach (var member in block.Members)
        {
            if (member is TopLevelMacroInvocationDeclaration inv)
            {
                var expanded = expander.ExpandTopLevel(inv.Invocation, currentDepth, inv.ExpansionOrigin, sourceText);

                if (expanded.Count == 1 && expanded[0] is TopLevelMacroInvocationDeclaration unexp && ReferenceEquals(unexp.Invocation, inv.Invocation))
                    newMembers.Add(member);
                else
                {
                    newMembers.AddRange(expanded);
                    changed = true;
                }
            }
            else
                newMembers.Add(member);
        }

        if (changed)
            block.Members = newMembers;

        return changed;
    }

    private static bool ExpandLocal(Local local, MacroExpander expander, ulong currentDepth, SourceText sourceText)
    {
        bool changed = false;

        if (local is LocalVariableDeclarationStatement varDecl)
        {
            foreach (var declarator in varDecl.Declaration.Declarators)
            {
                if (declarator.Initializer is { } init)
                {
                    var newExpr = ExpandExpression(init.Initializer, expander, currentDepth, sourceText, ref changed);

                    if (!ReferenceEquals(newExpr, init.Initializer))
                        init.Initializer = newExpr;
                }
            }
        }
        else if (local is LocalReturnStatement retStmt && retStmt.Statement.Expression is not null)
        {
            var newExpr = ExpandExpression(retStmt.Statement.Expression, expander, currentDepth, sourceText, ref changed);

            if (!ReferenceEquals(newExpr, retStmt.Statement.Expression))
                retStmt.Statement.Expression = newExpr;
        }
        else if (local is LocalExpressionStatement exprStmt && exprStmt.Expression is not MacroInvocationExpression)
        {
            var newExpr = ExpandExpression(exprStmt.Expression, expander, currentDepth, sourceText, ref changed);

            if (!ReferenceEquals(newExpr, exprStmt.Expression))
                exprStmt.Expression = newExpr;
        }

        return changed;
    }

    private static bool ExpandTopLevelStatement(TopLevelStatement stmt, MacroExpander expander, ulong currentDepth, SourceText sourceText)
    {
        bool changed = false;

        if (stmt is TopLevelExpressionStatement exprStmt)
        {
            var newExpr = ExpandExpression(exprStmt.Expression, expander, currentDepth, sourceText, ref changed);

            if (!ReferenceEquals(newExpr, exprStmt.Expression))
                exprStmt.Expression = newExpr;
        }

        return changed;
    }

    private static Expression ExpandExpression(Expression expr, MacroExpander expander, ulong currentDepth, SourceText sourceText, ref bool changed)
    {
        if (expr is MacroInvocationExpression inv)
        {
            var expanded = expander.ExpandExpression(inv, currentDepth, inv.ExpansionOrigin, sourceText);

            if (!ReferenceEquals(expanded, inv))
            {
                changed = true;
                return expanded;
            }

            return expanded;
        }

        if (expr is BinaryExpression bin)
        {
            var left = ExpandExpression(bin.LeftExpression, expander, currentDepth, sourceText, ref changed);

            if (!ReferenceEquals(left, bin.LeftExpression))
                bin.LeftExpression = left;

            var right = ExpandExpression(bin.RightExpression, expander, currentDepth, sourceText, ref changed);

            if (!ReferenceEquals(right, bin.RightExpression))
                bin.RightExpression = right;

            return bin;
        }

        if (expr is AssignmentExpression assign)
        {
            var rhs = ExpandExpression(assign.RhsExpression, expander, currentDepth, sourceText, ref changed);

            if (!ReferenceEquals(rhs, assign.RhsExpression))
                assign.RhsExpression = rhs;

            return assign;
        }

        if (expr is UnaryExpression unary)
        {
            var operand = ExpandExpression(unary.Operand, expander, currentDepth, sourceText, ref changed);

            if (!ReferenceEquals(operand, unary.Operand))
                unary.Operand = operand;

            return unary;
        }

        if (expr is ParenthesizedExpression paren)
        {
            var inner = ExpandExpression(paren.Expression, expander, currentDepth, sourceText, ref changed);

            if (!ReferenceEquals(inner, paren.Expression))
                paren.Expression = inner;

            return paren;
        }

        if (expr is NameofExpression nameofExpr)
        {
            var inner = ExpandExpression(nameofExpr.Argument, expander, currentDepth, sourceText, ref changed);

            if (!ReferenceEquals(inner, nameofExpr.Argument))
                nameofExpr.Argument = inner;

            return nameofExpr;
        }

        if (expr is CallExpression call)
        {
            var callee = ExpandExpression(call.Callee, expander, currentDepth, sourceText, ref changed);

            if (!ReferenceEquals(callee, call.Callee))
                call.Callee = callee;

            bool argsChanged = false;
            var newElements = new List<SyntaxNode>();

            for (int i = 0; i < call.Arguments.Count; i++)
            {
                var arg = call.Arguments[i];
                var expandedArg = ExpandExpression(arg, expander, currentDepth, sourceText, ref changed);

                if (!ReferenceEquals(expandedArg, arg))
                    argsChanged = true;

                newElements.Add(expandedArg);
                var sep = call.Arguments.GetSeparator(i);

                if (sep is not null)
                    newElements.Add(sep);
            }

            if (argsChanged)
                call.Arguments = new SeparatedSyntaxList<Expression>(newElements);

            return call;
        }

        if (expr is BlockExpression block)
        {
            bool localsChanged = false;
            var newLocals = new List<Local>();

            foreach (var local in block.Locals)
            {
                if (local is LocalMacroInvocationDeclaration localInv)
                {
                    var expanded = expander.ExpandStatement(localInv.Invocation, currentDepth, localInv.ExpansionOrigin, sourceText);

                    if (expanded.Count == 1 && expanded[0] is LocalMacroInvocationDeclaration unexp && ReferenceEquals(unexp.Invocation, localInv.Invocation))
                        newLocals.Add(local);
                    else
                    {
                        newLocals.AddRange(expanded);
                        localsChanged = true;
                    }
                }
                else if (local is LocalExpressionStatement exprStmt && exprStmt.Expression is MacroInvocationExpression innerInv)
                {
                    var expanded = expander.ExpandStatement(innerInv, currentDepth, exprStmt.ExpansionOrigin, sourceText);

                    if (expanded.Count == 1 && expanded[0] is LocalMacroInvocationDeclaration unexp && ReferenceEquals(unexp.Invocation, innerInv))
                        newLocals.Add(local);
                    else
                    {
                        newLocals.AddRange(expanded);
                        localsChanged = true;
                    }
                }
                else
                {
                    if (ExpandLocal(local, expander, currentDepth, sourceText))
                        localsChanged = true;

                    newLocals.Add(local);
                }
            }

            Expression? newFinal = block.FinalExpression;
            if (block.FinalExpression is not null)
            {
                newFinal = ExpandExpression(block.FinalExpression, expander, currentDepth, sourceText, ref changed);
            }

            if (localsChanged || !ReferenceEquals(newFinal, block.FinalExpression))
            {
                changed = true;
                return new BlockExpression(block.OpenBrace, newLocals, newFinal, block.CloseBrace)
                {
                    ExpansionOrigin = block.ExpansionOrigin
                };
            }

            return block;
        }

        return expr;
    }

    private static bool TryExtractMacroAttributeFromTopLevel(TopLevel member, SourceText sourceText, out MacroInvocationExpression? invocation)
    {
        invocation = null;
        IReadOnlyList<AttributeListSyntax>? attributeLists = null;

        if (member is TopLevelTypeDeclaration typeDecl)
            attributeLists = typeDecl.Type.Attributes;
        else if (member is TopLevelFunctionDeclaration funcDecl)
            attributeLists = funcDecl.Function.Attributes;
        else if (member is TopLevelVariableDeclaration varDecl)
            attributeLists = varDecl.Declaration.Attributes;

        if (attributeLists is null || attributeLists.Count == 0)
            return false;

        return TryBuildMacroAttributeInvocation(member, attributeLists, sourceText, out invocation);
    }

    private static bool TryExtractMacroAttributeFromMember(Member member, SourceText sourceText, out MacroInvocationExpression? invocation)
    {
        invocation = null;
        IReadOnlyList<AttributeListSyntax>? attributeLists = null;

        if (member is MemberTypeDeclaration typeDecl)
            attributeLists = typeDecl.Type.Attributes;
        else if (member is MemberFunctionDeclaration funcDecl)
            attributeLists = funcDecl.Function.Attributes;
        else if (member is MemberFieldDeclaration fieldDecl)
            attributeLists = fieldDecl.Declaration.Attributes;
        else if (member is MemberPropertyDeclaration propDecl)
            attributeLists = propDecl.Attributes;

        if (attributeLists is null || attributeLists.Count == 0)
            return false;

        return TryBuildMacroAttributeInvocation(member, attributeLists, sourceText, out invocation);
    }

    private static bool TryBuildMacroAttributeInvocation(
        SyntaxNode declNode,
        IReadOnlyList<AttributeListSyntax> attributeLists,
        SourceText sourceText,
        out MacroInvocationExpression? invocation)
    {
        invocation = null;

        for (int listIdx = 0; listIdx < attributeLists.Count; listIdx++)
        {
            var list = attributeLists[listIdx];
            for (int appIdx = 0; appIdx < list.Attributes.Count; appIdx++)
            {
                var app = list.Attributes[appIdx];
                if (app.IsMacroAttribute)
                {
                    var optDeclSpan = declNode.GetSpan();
                    if (!optDeclSpan.HasValue)
                        return false;

                    TextSpan declSpan = optDeclSpan.Value;

                    var optExcludeSpan = list.Attributes.Count == 1 ? list.GetSpan() : app.GetSpan();
                    if (!optExcludeSpan.HasValue)
                        return false;

                    TextSpan excludeSpan = optExcludeSpan.Value;

                    int beforeLength = Math.Max(0, excludeSpan.Start - declSpan.Start);
                    string beforeText = beforeLength > 0 ? sourceText.ToString(new TextSpan(declSpan.Start, beforeLength)) : string.Empty;

                    int afterStart = Math.Min(declSpan.End, excludeSpan.End);
                    int afterLength = Math.Max(0, declSpan.End - afterStart);
                    string afterText = afterLength > 0 ? sourceText.ToString(new TextSpan(afterStart, afterLength)) : string.Empty;

                    string targetText = (beforeText + " " + afterText).Trim();

                    var dummyDiags = new DiagnosticsManager();
                    var targetLexer = new Lexer(new SourceText(targetText), dummyDiags);
                    var targetTokens = targetLexer.Lex();
                    if (targetTokens.Count > 0 && targetTokens[^1].Kind is TokenKind.EndToken)
                        targetTokens.RemoveAt(targetTokens.Count - 1);

                    var allArgs = new List<MacroArgumentSyntax>();

                    foreach (var argExpr in app.Arguments)
                    {
                        var optArgSpan = argExpr.GetSpan();
                        if (optArgSpan.HasValue)
                        {
                            var argTokens = new Lexer(new SourceText(sourceText.ToString(optArgSpan.Value)), dummyDiags).Lex();
                            if (argTokens.Count > 0 && argTokens[^1].Kind is TokenKind.EndToken)
                                argTokens.RemoveAt(argTokens.Count - 1);

                            allArgs.Add(new MacroArgumentSyntax(argTokens));
                        }
                    }

                    allArgs.Add(new MacroArgumentSyntax(targetTokens));

                    var dollarToken = app.DollarToken ?? new Token(sourceText, app.Name.GetSpan() ?? default, TokenKind.Dollar, [], []);
                    var nameToken = GetNameToken(app.Name);
                    var leftParen = app.OpenParen ?? new Token(sourceText, default, TokenKind.LeftParen, [], []);
                    var rightParen = app.CloseParen ?? new Token(sourceText, default, TokenKind.RightParen, [], []);

                    invocation = new MacroInvocationExpression(dollarToken, nameToken, leftParen, allArgs, rightParen);
                    return true;
                }
            }
        }

        return false;
    }

    private static Token GetNameToken(NamedSyntax name) => name switch
    {
        SimpleName s => s.Name,
        GenericName g => g.Name,
        QualifiedName q when q.Parts.Count > 0 => GetNameToken(q.Parts[q.Parts.Count - 1]),
        _ => new Token(new SourceText(name.ToString() ?? string.Empty), default, TokenKind.Identifier, [], [])
    };
}
