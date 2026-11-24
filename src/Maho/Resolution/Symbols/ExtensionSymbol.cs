using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class ExtensionSymbol : Symbol
{
    public Accessibility Accessibility { get; internal set; } = Accessibility.Internal;
    public TypeSyntax TargetTypeSyntax { get; }
    public TypeRef TargetTypeRef { get; internal set; } = TypeRef.Unresolved;
    public SymbolHandle? TargetTypeHandle { get; internal set; }
    public IReadOnlyList<SymbolHandle> GenericParameters { get; internal set; }
    public List<TypeRef> BaseInterfaces { get; internal set; }
    public List<SymbolHandle> Methods { get; } = [];
    public List<SymbolHandle> Properties { get; } = [];
    public Scope Scope { get; }
    public ExtensionDeclaration? Syntax { get; }

    public ExtensionSymbol(SymbolID id, Scope enclosingScope, Scope extensionScope, SymbolPart name, TypeSyntax targetTypeSyntax, ExtensionDeclaration? syntax)
        : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.Extension;
        Scope = extensionScope;
        TargetTypeSyntax = targetTypeSyntax;
        Syntax = syntax;
        GenericParameters = [];
        BaseInterfaces = [];
    }
}

