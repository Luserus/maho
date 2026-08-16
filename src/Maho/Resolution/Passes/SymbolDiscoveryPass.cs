using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class SymbolDiscoveryPass : ResolutionPass
{
    public override void Resolve(ResolutionContext context)
    {
        foreach (var root in context.SyntaxTree.Roots)
        {
            ResolveCompilationUnit(root);
        }
    }

    private void ResolveCompilationUnit(CompilationUnit unit)
    {
        var scope = Scope.GlobalScope;

        foreach (var member in unit.Members)
        {
            ResolveTopLevel(member, scope);
        }
    }

    private void ResolveTopLevel(TopLevel topLevel, Scope scope)
    {
        switch (topLevel)
        {
            case TopLevelTypeDeclaration type:
                ResolveTypeDeclaration(type.Type, scope);
                break;
        }
    }

    private void ResolveTypeDeclaration(TypeDeclaration declaration, Scope scope)
    {
        
    }
}

