namespace Maho.Syntax;

/// <summary>
/// Groups every parsed compilation unit that participates in one syntax-analysis result. Resolution
/// starts from this container so semantic passes only run after parsing has finished for the whole
/// batch of source files.
/// </summary>
internal sealed class SyntaxTree : SyntaxNode
{
    public string Name { get; }
    public CompilationUnit[] Roots { get; }

    public SyntaxTree(string name, CompilationUnit[] roots)
    {
        Name = name;
        Roots = roots;
    }

    public static SyntaxTree CreateSingleRoot(CompilationUnit root, string name = "<single-file>") => new(name, [root]);
}
