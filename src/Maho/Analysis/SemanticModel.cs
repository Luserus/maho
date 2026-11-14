using System;
using System.Collections.Generic;
using System.Linq;
using Maho.Resolution;
using Maho.Resolution.ControlFlow;
using Maho.Syntax;
using Maho.Text;
using Maho.Tooling;

namespace Maho;

/// <summary>
/// Provides semantic information and bindings for a specific <see cref="SyntaxTree"/> within a <see cref="Compilation"/>.
/// </summary>
public sealed class SemanticModel
{
    /// <summary> Gets the parent <see cref="Compilation"/> owning this semantic model. </summary>
    public Compilation Compilation { get; }

    /// <summary> Gets the <see cref="SyntaxTree"/> for which this semantic model provides bindings. </summary>
    public SyntaxTree SyntaxTree { get; }

    internal Dictionary<SyntaxNode, SymbolHandle> NodeSymbols { get; }
    internal Dictionary<Expression, TypeRef> ExpressionTypes { get; }
    internal Dictionary<SymbolHandle, ControlFlowGraph> ControlFlowGraphs { get; }

    /// <summary> Gets the syntax and semantic diagnostics reported for this syntax tree. </summary>
    public IReadOnlyList<DiagnosticInfo> Diagnostics => SyntaxTree.Diagnostics;

    internal SemanticModel(
        Compilation compilation,
        SyntaxTree syntaxTree,
        Dictionary<SyntaxNode, SymbolHandle> nodeSymbols,
        Dictionary<Expression, TypeRef> expressionTypes,
        Dictionary<SymbolHandle, ControlFlowGraph> controlFlowGraphs)
    {
        Compilation = compilation;
        SyntaxTree = syntaxTree;
        NodeSymbols = nodeSymbols;
        ExpressionTypes = expressionTypes;
        ControlFlowGraphs = controlFlowGraphs;
    }

    /// <summary> Gets the resolved symbol for an AST node within this tree. </summary>
    internal Symbol? GetSymbol(SyntaxNode node)
    {
        if (NodeSymbols.TryGetValue(node, out var handle))
            return Compilation.Context.GetSymbol(handle);

        return FindDeclaredSymbol(node);
    }

    /// <summary> Gets the semantic type of an expression within this tree. </summary>
    internal TypeRef? GetTypeInfo(Expression expr) =>
        ExpressionTypes.TryGetValue(expr, out var type) ? type : null;

    /// <summary> Gets the control flow graph for a function declared in this tree. </summary>
    internal ControlFlowGraph? GetControlFlowGraph(SymbolHandle functionHandle) =>
        ControlFlowGraphs.TryGetValue(functionHandle, out var cfg) ? cfg : null;

    /// <summary>
    /// Finds the innermost syntax node and its chain of ancestor nodes at the given 1-based line and column.
    /// </summary>
    internal (SyntaxNode Node, List<SyntaxNode> Ancestors)? FindNodeAndAncestorsAt(int line, int column)
    {
        var lines = SyntaxTree.SourceText.Lines;
        if (line < 1 || line > lines.Length)
            return null;

        var lineObj = lines[line - 1];
        int offset = lineObj.Start + Math.Max(0, column - 1);
        if (offset > SyntaxTree.SourceText.Length)
            offset = SyntaxTree.SourceText.Length;

        var ancestors = new List<SyntaxNode>();
        SyntaxNode current = SyntaxTree.Root;

        while (true)
        {
            SyntaxNode? matchingChild = null;
            foreach (var (_, child) in SyntaxSpan.GetChildren(current))
            {
                var span = child.GetSpan();
                if (span.HasValue && offset >= span.Value.Start && offset <= span.Value.End)
                {
                    matchingChild = child;
                    break;
                }
            }

            if (matchingChild is null)
                break;

            ancestors.Add(current);
            current = matchingChild;
        }

        return (current, ancestors);
    }

    /// <summary>
    /// LSP Go-to-Definition: resolves the definition location for the symbol at the given 1-based line and column.
    /// </summary>
    /// <param name="line">The 1-based line number.</param>
    /// <param name="column">The 1-based column number.</param>
    /// <returns>A <see cref="DefinitionLocation"/> if resolved; otherwise <see langword="null"/>.</returns>
    public DefinitionLocation? FindDefinition(int line, int column)
    {
        var hit = FindNodeAndAncestorsAt(line, column);
        if (hit is null)
            return null;

        var (node, ancestors) = hit.Value;
        Symbol? symbol = ResolveSymbolAtNode(node, ancestors);
        if (symbol is null)
            return null;

        var defSyntax = GetSymbolDeclarationSyntax(symbol);
        var defSource = defSyntax?.GetSource();
        string defPath = defSource?.FilePath ?? SyntaxTree.FilePath;
        var defSpan = defSyntax?.GetSpan() ?? default;
        var nameSpan = GetSymbolNameSpan(symbol) ?? defSpan;

        var spanInfo = TextSpanInfo.FromSpan(defSpan, defSource ?? SyntaxTree.SourceText);
        var selInfo = TextSpanInfo.FromSpan(nameSpan, defSource ?? SyntaxTree.SourceText);
        string? containerName = GetContainerName(symbol);

        return new DefinitionLocation(
            defPath,
            spanInfo,
            selInfo,
            symbol.Name.Text,
            symbol.Kind.ToString(),
            containerName);
    }

    /// <summary>
    /// LSP Find-All-References: finds all references across the compilation to the symbol at the given 1-based line and column.
    /// </summary>
    /// <param name="line">The 1-based line number.</param>
    /// <param name="column">The 1-based column number.</param>
    /// <param name="includeDeclaration">Whether to include the symbol's declaration in the results.</param>
    /// <returns>A list of reference locations matching the symbol.</returns>
    public IReadOnlyList<ReferenceLocation> FindReferences(int line, int column, bool includeDeclaration = true)
    {
        var hit = FindNodeAndAncestorsAt(line, column);
        if (hit is null)
            return [];

        var (node, ancestors) = hit.Value;
        Symbol? symbol = ResolveSymbolAtNode(node, ancestors);
        if (symbol is null)
            return [];

        var targetHandle = ResolutionContext.GetHandle(symbol);
        var results = new List<ReferenceLocation>();

        if (includeDeclaration)
        {
            var defSyntax = GetSymbolDeclarationSyntax(symbol);
            if (defSyntax != null)
            {
                var defSource = defSyntax.GetSource();
                string defPath = defSource?.FilePath ?? SyntaxTree.FilePath;
                var defSpan = GetSymbolNameSpan(symbol) ?? defSyntax.GetSpan() ?? default;
                var spanInfo = TextSpanInfo.FromSpan(defSpan, defSource ?? SyntaxTree.SourceText);
                string? contextLine = GetContextLine(defSource ?? SyntaxTree.SourceText, defSpan);
                results.Add(new ReferenceLocation(defPath, spanInfo, true, contextLine));
            }
        }

        foreach (var tree in Compilation.SyntaxTrees)
        {
            var model = Compilation.GetSemanticModel(tree);
            if (model is null)
                continue;

            foreach (var (refNode, boundHandle) in model.NodeSymbols)
            {
                if (boundHandle == targetHandle)
                {
                    var span = refNode.GetSpan() ?? default;
                    var spanInfo = TextSpanInfo.FromSpan(span, tree.SourceText);
                    string? contextLine = GetContextLine(tree.SourceText, span);
                    results.Add(new ReferenceLocation(tree.FilePath, spanInfo, false, contextLine));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// LSP Hover: gets hover tooltip information for the symbol or expression at the given 1-based line and column.
    /// </summary>
    /// <param name="line">The 1-based line number.</param>
    /// <param name="column">The 1-based column number.</param>
    /// <returns>Hover tooltip information, or <see langword="null"/> if no symbol or typed expression is found at the position.</returns>
    public HoverInfo? GetHover(int line, int column)
    {
        var hit = FindNodeAndAncestorsAt(line, column);
        if (hit is null)
            return null;

        var (node, ancestors) = hit.Value;
        var nodeSpan = node.GetSpan() ?? default;
        var spanInfo = TextSpanInfo.FromSpan(nodeSpan, SyntaxTree.SourceText);

        Symbol? symbol = ResolveSymbolAtNode(node, ancestors);
        if (symbol is not null)
        {
            string display = FormatSymbolHover(symbol);
            string? qualType = GetQualifiedSymbolType(symbol);
            string? doc = GetSymbolDocumentation(symbol);
            return new HoverInfo(spanInfo, display, qualType, doc);
        }

        // Fall back to expression type hover
        if (node is Expression expr && ExpressionTypes.TryGetValue(expr, out var exprType))
        {
            string qualType = exprType.ToDisplayString(Compilation.Context);
            return new HoverInfo(spanInfo, $"{exprType.ToDisplayString(Compilation.Context)}", qualType, null);
        }

        foreach (var ancestor in ancestors.AsEnumerable().Reverse())
        {
            if (ancestor is Expression aExpr && ExpressionTypes.TryGetValue(aExpr, out var aType))
            {
                string qualType = aType.ToDisplayString(Compilation.Context);
                return new HoverInfo(spanInfo, $"{aType.ToDisplayString(Compilation.Context)}", qualType, null);
            }
        }

        return null;
    }

    /// <summary>
    /// LSP Document Symbols: retrieves hierarchical symbols for the document outline.
    /// </summary>
    /// <returns>A hierarchical outline list of document symbols.</returns>
    public IReadOnlyList<DocumentSymbolInfo> GetDocumentSymbols()
    {
        var results = new List<DocumentSymbolInfo>();
        ExtractDocumentSymbols(SyntaxTree.Root.Members, results);
        return results;
    }

    /// <summary>
    /// LSP Completions: provides completion items at the given 1-based line and column.
    /// </summary>
    /// <param name="line">The 1-based line number.</param>
    /// <param name="column">The 1-based column number.</param>
    /// <returns>A list of completion suggestions.</returns>
    public IReadOnlyList<CompletionItemInfo> GetCompletions(int line, int column)
    {
        var results = new List<CompletionItemInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // 1. Check if dot-completion (preceded by '.')
        var lines = SyntaxTree.SourceText.Lines;
        if (line >= 1 && line <= lines.Length)
        {
            int offset = lines[line - 1].Start + Math.Max(0, column - 1);
            int checkPos = offset - 1;
            while (checkPos >= 0 && char.IsWhiteSpace(SyntaxTree.SourceText[checkPos]))
                checkPos--;

            if (checkPos >= 0 && SyntaxTree.SourceText[checkPos] == '.')
            {
                var hit = FindNodeAndAncestorsAt(line, column > 1 ? column - 1 : column);
                if (hit != null)
                {
                    var receiver = hit.Value.Ancestors.OfType<MemberAccessExpression>().FirstOrDefault()?.Expression
                        ?? hit.Value.Node as Expression;
                    if (receiver != null && ExpressionTypes.TryGetValue(receiver, out var receiverType))
                    {
                        AddTypeMemberCompletions(receiverType, results, seen);
                        return results;
                    }
                }
            }
        }

        // 2. Scope completions (keywords, symbols in compilation)
        string[] keywords = [
            "func", "struct", "class", "interface", "enum", "type",
            "if", "else", "match", "return", "let", "mut", "var",
            "while", "for", "in", "break", "continue", "import", "public", "private"
        ];

        foreach (var kw in keywords)
        {
            if (seen.Add(kw))
                results.Add(new CompletionItemInfo(kw, "keyword", "keyword"));
        }

        foreach (var f in Compilation.Context.FunctionSymbols)
        {
            if (seen.Add(f.Name.Text))
                results.Add(new CompletionItemInfo(f.Name.Text, "function", f.ReturnType.ToDisplayString(Compilation.Context)));
        }

        foreach (var t in Compilation.Context.TypeSymbols)
        {
            if (seen.Add(t.Name.Text))
                results.Add(new CompletionItemInfo(t.Name.Text, t.TypeKind == Resolution.TypeKind.Enum ? "enum" : "struct", t.Name.Text));
        }

        foreach (var g in Compilation.Context.GlobalVariableSymbols)
        {
            if (seen.Add(g.Name.Text))
                results.Add(new CompletionItemInfo(g.Name.Text, "variable", g.Type.ToDisplayString(Compilation.Context)));
        }

        return results;
    }

    /// <summary>
    /// LSP Signature Help: provides signature help and active parameter index for a call at the given line/column.
    /// </summary>
    /// <param name="line">The 1-based line number.</param>
    /// <param name="column">The 1-based column number.</param>
    /// <returns>Signature help information for the active parameter and signatures, or <see langword="null"/> if not in a call.</returns>
    public SignatureHelpInfo? GetSignatureHelp(int line, int column)
    {
        var hit = FindNodeAndAncestorsAt(line, column);
        if (hit is null)
            return null;

        var call = hit.Value.Ancestors.OfType<CallExpression>().LastOrDefault();
        if (call is null && hit.Value.Node is CallExpression ce)
            call = ce;

        if (call is null)
            return null;

        Symbol? calleeSymbol = null;
        if (NodeSymbols.TryGetValue(call, out var callHandle))
            calleeSymbol = Compilation.Context.GetSymbol(callHandle);
        else if (call.Callee is not null && NodeSymbols.TryGetValue(call.Callee, out var calleeHandle))
            calleeSymbol = Compilation.Context.GetSymbol(calleeHandle);

        if (calleeSymbol is null)
            return null;

        int activeParam = 0;
        int offset = SyntaxTree.SourceText.Lines[line - 1].Start + Math.Max(0, column - 1);
        if (call.Arguments.Count != 0)
        {
            for (int i = 0; i < call.Arguments.Count; i++)
            {
                var argSpan = call.Arguments[i].GetSpan();
                if (argSpan.HasValue && offset > argSpan.Value.End)
                    activeParam = i + 1;
            }
        }

        var sig = CreateSignatureItem(calleeSymbol);
        return new SignatureHelpInfo([sig], 0, activeParam);
    }

    /// <summary>
    /// LSP Semantic Tokens: generates semantic classification tokens for the entire syntax tree.
    /// </summary>
    /// <returns>A list of classified semantic highlighting tokens ordered by line and column.</returns>
    public IReadOnlyList<SemanticTokenInfo> GetSemanticTokens()
    {
        var tokens = new List<SemanticTokenInfo>();

        foreach (var (node, handle) in NodeSymbols)
        {
            var span = node.GetSpan();
            if (!span.HasValue || span.Value.Length == 0)
                continue;

            int line = span.Value.GetStartLine(SyntaxTree.SourceText) + 1;
            int col = span.Value.GetStartColumn(SyntaxTree.SourceText) + 1;

            string tokenType = handle.Kind switch
            {
                SymbolKind.Function or SymbolKind.Method => "function",
                SymbolKind.Type or SymbolKind.NestedType => "type",
                SymbolKind.GlobalVariable or SymbolKind.Variable => "variable",
                SymbolKind.Parameter => "parameter",
                SymbolKind.Field => "property",
                SymbolKind.Property => "property",
                SymbolKind.EnumVariant => "enumMember",
                SymbolKind.Macro => "macro",
                _ => "variable"
            };

            tokens.Add(new SemanticTokenInfo(line, col, span.Value.Length, tokenType));
        }

        return tokens.OrderBy(t => t.Line).ThenBy(t => t.Column).ToList();
    }

    // --- Private Helpers ---

    private Symbol? ResolveSymbolAtNode(SyntaxNode node, List<SyntaxNode> ancestors)
    {
        if (NodeSymbols.TryGetValue(node, out var handle))
            return Compilation.Context.GetSymbol(handle);

        var declared = FindDeclaredSymbol(node);
        if (declared != null)
            return declared;

        foreach (var ancestor in ancestors.AsEnumerable().Reverse())
        {
            if (NodeSymbols.TryGetValue(ancestor, out handle))
                return Compilation.Context.GetSymbol(handle);

            declared = FindDeclaredSymbol(ancestor);
            if (declared != null)
                return declared;
        }

        return null;
    }

    private Symbol? FindDeclaredSymbol(SyntaxNode node)
    {
        var context = Compilation.Context;
        return node switch
        {
            TopLevelTypeDeclaration topType => FindDeclaredSymbol(topType.Type),
            TopLevelFunctionDeclaration topFn => FindDeclaredSymbol(topFn.Function),
            TopLevelVariableDeclaration topVar => FindDeclaredSymbol(topVar.Declaration),
            MemberFunctionDeclaration memFn => FindDeclaredSymbol(memFn.Function),
            MemberFieldDeclaration memFld => FindDeclaredSymbol(memFld.Declaration),
            TypeDeclaration td => context.TypeSymbols.FirstOrDefault(t => t.Syntax == td)
                ?? (Symbol?)context.NestedTypeSymbols.FirstOrDefault(nt => nt.Syntax == td),
            FunctionDeclaration fd => context.FunctionSymbols.FirstOrDefault(f => f.Syntax == fd)
                ?? (Symbol?)context.MethodSymbols.FirstOrDefault(m => m.Syntax == fd),
            VariableDeclaration vd => context.GlobalVariableSymbols.FirstOrDefault(g => g.Syntax == vd)
                ?? (Symbol?)context.FieldSymbols.FirstOrDefault(f => f.Syntax == vd),
            Parameter p => context.ParameterSymbols.FirstOrDefault(ps => ps.Syntax == p),
            _ => null
        };
    }

    private static SyntaxNode? GetSymbolDeclarationSyntax(Symbol symbol) => symbol switch
    {
        TypeSymbol t => t.Syntax,
        NestedTypeSymbol nt => nt.Syntax,
        FunctionSymbol f => f.Syntax,
        MethodSymbol m => m.Syntax,
        GlobalVariableSymbol g => g.Syntax,
        FieldSymbol fld => fld.Syntax,
        ParameterSymbol p => p.Syntax,
        AliasSymbol a => a.Syntax,
        _ => null
    };

    private static TextSpan? GetSymbolNameSpan(Symbol symbol) => symbol switch
    {
        TypeSymbol t => t.Syntax?.Name.GetSpan(),
        NestedTypeSymbol nt => nt.Syntax?.Name.GetSpan(),
        FunctionSymbol f => f.Syntax?.Signature.Identifier.GetSpan(),
        MethodSymbol m => m.Syntax?.Signature.Identifier.GetSpan(),
        GlobalVariableSymbol g => g.Syntax?.Declarator.Identifier.GetSpan(),
        FieldSymbol fld => fld.Syntax?.Declarator.Identifier.GetSpan(),
        ParameterSymbol p => p.Syntax?.Declarator.Identifier.GetSpan(),
        AliasSymbol a => a.Syntax?.Name.GetSpan(),
        _ => null
    };

    private string? GetContainerName(Symbol symbol) => symbol switch
    {
        NestedTypeSymbol nt => nt.Parent.HasValue ? Compilation.Context.GetSymbol(nt.Parent.Value)?.Name.Text : null,
        MethodSymbol m => m.Parent.HasValue ? Compilation.Context.GetSymbol(m.Parent.Value)?.Name.Text : null,
        FieldSymbol fld => fld.Parent.HasValue ? Compilation.Context.GetSymbol(fld.Parent.Value)?.Name.Text : null,
        TypeSymbol t => t.ContainingNamespace != null && t.ContainingNamespace != Compilation.Context.GlobalNamespace ? ResolutionContext.GetNamespacePath(t.ContainingNamespace, Compilation.Context.GlobalNamespace) : null,
        FunctionSymbol f => f.ContainingNamespace != null && f.ContainingNamespace != Compilation.Context.GlobalNamespace ? ResolutionContext.GetNamespacePath(f.ContainingNamespace, Compilation.Context.GlobalNamespace) : null,
        GlobalVariableSymbol g => g.ContainingNamespace != null && g.ContainingNamespace != Compilation.Context.GlobalNamespace ? ResolutionContext.GetNamespacePath(g.ContainingNamespace, Compilation.Context.GlobalNamespace) : null,
        _ => null
    };

    private static string? GetContextLine(SourceText text, TextSpan span)
    {
        int lineIdx = span.GetStartLine(text);
        if (lineIdx >= 0 && lineIdx < text.Lines.Length)
            return text.Lines[lineIdx].ToString().TrimEnd('\r', '\n');
        return null;
    }

    private string FormatSymbolHover(Symbol symbol) => symbol switch
    {
        FunctionSymbol f => $"func {f.Name.Text}({string.Join(", ", f.Parameters.Select(Compilation.Context.GetParameterSymbol).Where(p => p != null).Select(p => $"{p!.Name.Text}: {p.Type.ToDisplayString(Compilation.Context)}"))}) -> {f.ReturnType.ToDisplayString(Compilation.Context)}",
        MethodSymbol m => $"func {m.Name.Text}({string.Join(", ", m.Parameters.Select(Compilation.Context.GetParameterSymbol).Where(p => p != null).Select(p => $"{p!.Name.Text}: {p.Type.ToDisplayString(Compilation.Context)}"))}) -> {m.ReturnType.ToDisplayString(Compilation.Context)}",
        TypeSymbol t => $"{t.TypeKind.ToString().ToLowerInvariant()} {t.Name.Text}",
        NestedTypeSymbol nt => $"{nt.TypeKind.ToString().ToLowerInvariant()} {nt.Name.Text}",
        GlobalVariableSymbol g => $"global {g.Name.Text}: {g.Type.ToDisplayString(Compilation.Context)}",
        FieldSymbol fld => $"field {fld.Name.Text}: {fld.Type.ToDisplayString(Compilation.Context)}",
        ParameterSymbol p => $"parameter {p.Name.Text}: {p.Type.ToDisplayString(Compilation.Context)}",
        PropertySymbol prop => $"property {prop.Name.Text}: {prop.Type.ToDisplayString(Compilation.Context)}",
        AliasSymbol a => $"alias {a.Name.Text} = {a.Target.ToDisplayString(Compilation.Context)}",
        _ => symbol.Name.Text
    };

    private string? GetQualifiedSymbolType(Symbol symbol) => symbol switch
    {
        FunctionSymbol f => f.ReturnType.ToDisplayString(Compilation.Context),
        MethodSymbol m => m.ReturnType.ToDisplayString(Compilation.Context),
        TypeSymbol t => ResolutionContext.GetQualifiedSymbolName(t, Compilation.Context),
        NestedTypeSymbol nt => ResolutionContext.GetQualifiedSymbolName(nt, Compilation.Context),
        GlobalVariableSymbol g => g.Type.ToDisplayString(Compilation.Context),
        FieldSymbol fld => fld.Type.ToDisplayString(Compilation.Context),
        ParameterSymbol p => p.Type.ToDisplayString(Compilation.Context),
        PropertySymbol prop => prop.Type.ToDisplayString(Compilation.Context),
        _ => null
    };

    private static string? GetSymbolDocumentation(Symbol symbol)
    {
        var syntax = GetSymbolDeclarationSyntax(symbol);
        if (syntax == null)
            return null;

        var token = SyntaxSpan.GetFirstToken(syntax);
        if (token != null && token.LeadingTrivia != null && token.LeadingTrivia.Length > 0)
        {
            var source = syntax.GetSource() ?? token.Source;
            var docLines = token.LeadingTrivia
                .Where(tr => tr.Kind == SyntaxTriviaKind.SingleLineComment || tr.Kind == SyntaxTriviaKind.MultiLineComment)
                .Select(tr => source.ToString(tr.Span).TrimStart('/', '*', ' '))
                .ToList();

            if (docLines.Count > 0)
                return string.Join(Environment.NewLine, docLines);
        }

        return null;
    }

    private static string GetNameText(NamedSyntax? name) => name switch
    {
        SimpleName s => s.Name.Value,
        GenericName g => g.Name.Value,
        QualifiedName q => string.Join(".", q.Parts.Select(GetNameText)),
        _ => name?.ToString() ?? string.Empty
    };

    private void ExtractDocumentSymbols(IEnumerable<TopLevel> members, List<DocumentSymbolInfo> results)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case TopLevelTypeDeclaration topType:
                    {
                        var td = topType.Type;
                        var span = td.GetSpan() ?? default;
                        var nameSpan = td.Name.GetSpan() ?? span;
                        var children = new List<DocumentSymbolInfo>();

                        if (td.Body is TypeBlockBody block)
                        {
                            foreach (var m in block.Members)
                            {
                                if (m is MemberFunctionDeclaration memFn)
                                {
                                    var fn = memFn.Function;
                                    var mSpan = fn.GetSpan() ?? default;
                                    var mNameSpan = fn.Signature.Identifier.GetSpan() ?? mSpan;
                                    children.Add(new DocumentSymbolInfo(
                                        GetNameText(fn.Signature.Identifier),
                                        "method",
                                        TextSpanInfo.FromSpan(mSpan, SyntaxTree.SourceText),
                                        TextSpanInfo.FromSpan(mNameSpan, SyntaxTree.SourceText),
                                        fn.Signature.ReturnType?.ToString()));
                                }
                                else if (m is MemberFieldDeclaration memFld)
                                {
                                    var fld = memFld.Declaration;
                                    var fSpan = fld.GetSpan() ?? default;
                                    var fNameSpan = fld.Declarator.Identifier.GetSpan() ?? fSpan;
                                    children.Add(new DocumentSymbolInfo(
                                        GetNameText(fld.Declarator.Identifier),
                                        "field",
                                        TextSpanInfo.FromSpan(fSpan, SyntaxTree.SourceText),
                                        TextSpanInfo.FromSpan(fNameSpan, SyntaxTree.SourceText),
                                        fld.Type.ToString()));
                                }
                            }
                        }

                        results.Add(new DocumentSymbolInfo(
                            GetNameText(td.Name),
                            td.Kind.ToString().ToLowerInvariant(),
                            TextSpanInfo.FromSpan(span, SyntaxTree.SourceText),
                            TextSpanInfo.FromSpan(nameSpan, SyntaxTree.SourceText),
                            null,
                            children.Count > 0 ? children : null));
                        break;
                    }
                case TopLevelFunctionDeclaration topFn:
                    {
                        var fn = topFn.Function;
                        var span = fn.GetSpan() ?? default;
                        var nameSpan = fn.Signature.Identifier.GetSpan() ?? span;
                        results.Add(new DocumentSymbolInfo(
                            GetNameText(fn.Signature.Identifier),
                            "function",
                            TextSpanInfo.FromSpan(span, SyntaxTree.SourceText),
                            TextSpanInfo.FromSpan(nameSpan, SyntaxTree.SourceText),
                            fn.Signature.ReturnType?.ToString()));
                        break;
                    }
                case TopLevelVariableDeclaration topVar:
                    {
                        var varDecl = topVar.Declaration;
                        var span = varDecl.GetSpan() ?? default;
                        var nameSpan = varDecl.Declarator.Identifier.GetSpan() ?? span;
                        results.Add(new DocumentSymbolInfo(
                            GetNameText(varDecl.Declarator.Identifier),
                            "variable",
                            TextSpanInfo.FromSpan(span, SyntaxTree.SourceText),
                            TextSpanInfo.FromSpan(nameSpan, SyntaxTree.SourceText),
                            varDecl.Type.ToString()));
                        break;
                    }
            }
        }
    }

    private void AddTypeMemberCompletions(TypeRef type, List<CompletionItemInfo> results, HashSet<string> seen)
    {
        var handle = Compilation.Context.GetType(type);
        if (!handle.HasValue)
            return;

        foreach (var m in Compilation.Context.MethodSymbols)
        {
            if (m.Parent == handle && seen.Add(m.Name.Text))
            {
                results.Add(new CompletionItemInfo(
                    m.Name.Text,
                    "method",
                    m.ReturnType.ToDisplayString(Compilation.Context),
                    null,
                    $"{m.Name.Text}()"));
            }
        }

        foreach (var fld in Compilation.Context.FieldSymbols)
        {
            if (fld.Parent == handle && seen.Add(fld.Name.Text))
            {
                results.Add(new CompletionItemInfo(
                    fld.Name.Text,
                    "field",
                    fld.Type.ToDisplayString(Compilation.Context)));
            }
        }
    }

    private SignatureItemInfo CreateSignatureItem(Symbol symbol)
    {
        var parameters = new List<ParameterItemInfo>();
        string label = symbol.Name.Text;

        if (symbol is FunctionSymbol f)
        {
            foreach (var h in f.Parameters)
            {
                if (Compilation.Context.GetParameterSymbol(h) is { } p)
                    parameters.Add(new ParameterItemInfo($"{p.Name.Text}: {p.Type.ToDisplayString(Compilation.Context)}", null));
            }
            label = $"func {f.Name.Text}({string.Join(", ", parameters.Select(p => p.Label))}) -> {f.ReturnType.ToDisplayString(Compilation.Context)}";
        }
        else if (symbol is MethodSymbol m)
        {
            foreach (var h in m.Parameters)
            {
                if (Compilation.Context.GetParameterSymbol(h) is { } p)
                    parameters.Add(new ParameterItemInfo($"{p.Name.Text}: {p.Type.ToDisplayString(Compilation.Context)}", null));
            }
            label = $"func {m.Name.Text}({string.Join(", ", parameters.Select(p => p.Label))}) -> {m.ReturnType.ToDisplayString(Compilation.Context)}";
        }

        return new SignatureItemInfo(label, null, parameters);
    }
}
