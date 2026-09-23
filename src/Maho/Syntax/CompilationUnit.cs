using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Root syntax node for one parsed source file. </summary>
internal sealed class CompilationUnit : SyntaxNode
{
    /// <summary> File-level directives that configure analysis or import namespaces for this compilation unit. </summary>
    public IReadOnlyList<Directive> Directives { get; }
    /// <summary> File-level pragmas that configure analysis of this compilation unit. </summary>
    public IReadOnlyList<PragmaDirective> Pragmas { get; }
    /// <summary> File-level using directives that import namespaces for this compilation unit. </summary>
    public IReadOnlyList<UsingDirective> Usings { get; }
    /// <summary> Top-level members contained in the file. </summary>
    public IReadOnlyList<TopLevel> Members { get; internal set; }
    /// <summary> Synthetic end-of-file token closing the unit. </summary>
    public Token EndToken { get; }
    /// <summary> Whether executable top-level statements are enabled for this unit. </summary>
    public bool EnablesTopLevelStatements { get; set; }

    /// <summary> Creates one compilation unit from its directives, members, and end token. </summary>
    public CompilationUnit(IReadOnlyList<Directive> directives, IReadOnlyList<TopLevel> members, Token endToken, bool enablesTopLevelStatements = false)
    {
        Directives = directives;
        var pragmas = new List<PragmaDirective>();
        var usings = new List<UsingDirective>();
        foreach (var directive in directives)
        {
            if (directive is PragmaDirective pragma)
                pragmas.Add(pragma);
            else if (directive is UsingDirective @using)
                usings.Add(@using);
        }
        Pragmas = pragmas;
        Usings = usings;
        Members = members;
        EndToken = endToken;
        EnablesTopLevelStatements = enablesTopLevelStatements;
    }

    /// <summary> Backward-compatible constructor accepting pragmas directly. </summary>
    public CompilationUnit(IReadOnlyList<PragmaDirective> pragmas, IReadOnlyList<TopLevel> members, Token endToken, bool enablesTopLevelStatements = false)
        : this(new List<Directive>(pragmas), members, endToken, enablesTopLevelStatements)
    {
    }
}