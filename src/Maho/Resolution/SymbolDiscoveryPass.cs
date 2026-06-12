using Maho.Syntax;

namespace Maho.Resolution;

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
        var scope = Scope.GlobalScope;

        foreach (var member in unit.Members)
        {
            ResolveTopLevel(member);
        }
    }

    private void ResolveTopLevel(TopLevel topLevel)
    {
        switch (topLevel)
        {
            case TopLevelTypeDeclaration type:

                break;
        }
    }
}

