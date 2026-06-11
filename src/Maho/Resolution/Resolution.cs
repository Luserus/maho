using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class Resolver
{
    private ResolutionPass[] passes =
    [
        new SymbolDiscoveryPass()
    ];

    private ResolvedTree resolvedTree = new ResolvedTree();
    private ResolutionMetadata metadata = new ResolutionMetadata();

    public void Resolve(SyntaxTree syntaxTree)
    {
        var context = new ResolutionContext(syntaxTree, metadata, resolvedTree);

        foreach (var pass in passes)
            pass.Resolve(context);

    }
}

internal abstract class ResolutionPass
{
    public abstract void Resolve(ResolutionContext context);
}

internal sealed class SymbolDiscoveryPass : ResolutionPass
{
    public override void Resolve(ResolutionContext context)
    {
        foreach (var root in context.syntaxTree.Roots)
        {
            ResolveCompilationUnit(root);
        }
    }

    private void ResolveCompilationUnit(CompilationUnit unit)
    {
        foreach (var member in unit.Members)
        {
            ResolveTopLevel(member);
        }
    }

    private void ResolveTopLevel(TopLevel topLevel)
    {
    }
}

internal abstract class ResolvedNode;

internal sealed class ResolvedTree
{

}

internal sealed class ResolutionMetadata
{
    // public List<Symbol> Symbols = [];
}

internal struct SymbolID
{
    public int Value;
}

internal sealed class ResolutionContext
{
    public SyntaxTree syntaxTree { get; }
    public ResolutionMetadata metadata { get; }
    public ResolvedTree resolvedTree { get; }

    public ResolutionContext(SyntaxTree syntaxTree, ResolutionMetadata metadata, ResolvedTree resolvedTree)
    {
        this.syntaxTree = syntaxTree;
        this.metadata = metadata;
        this.resolvedTree = resolvedTree;
    }
}
