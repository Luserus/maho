using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class Resolver
{
    private readonly ResolutionPass[] passes =
    [
        new SymbolDiscoveryPass(),
        new DeclarationResolutionPass()
    ];

    private readonly ResolvedTree resolvedTree = new ResolvedTree();

    public ResolutionContext Resolve(
        SyntaxTree syntaxTree,
        IReadOnlyList<ResolutionContext>? referencedProjects = null,
        ResolutionContext? baseContext = null)
    {
        var resolved = baseContext?.ResolvedTree ?? resolvedTree;
        var globalNamespace = baseContext?.GlobalNamespace ?? new NamespaceTrieNode();
        SymbolStore symbolStore;
        List<Scope> scopes;

        if (baseContext != null)
        {
            symbolStore = new SymbolStore(
                baseContext.AttributeSymbols,
                baseContext.NestedAttributeSymbols,
                baseContext.TypeSymbols,
                baseContext.NestedTypeSymbols,
                baseContext.FunctionSymbols,
                baseContext.MethodSymbols,
                baseContext.GlobalVariableSymbols,
                baseContext.FieldSymbols,
                baseContext.ParameterSymbols,
                baseContext.LocalVariableSymbols,
                baseContext.PropertySymbols,
                baseContext.GenericParameterSymbols,
                baseContext.LabelSymbols,
                baseContext.AliasSymbols
            );
            scopes = baseContext.Scopes;
        }
        else
        {
            symbolStore = SymbolStore.CreateEmpty();
            scopes = [new Scope(null)];
        }

        var context = new ResolutionContext(
            syntaxTree,
            resolved,
            globalNamespace,
            symbolStore,
            scopes,
            referencedProjects ?? baseContext?.ReferencedProjects,
            baseContext?.ImportedProjectSymbols);

        foreach (var pass in passes)
            pass.Resolve(context);

        return context;
    }
}