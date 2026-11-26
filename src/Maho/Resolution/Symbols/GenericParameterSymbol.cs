using System.Collections.Generic;
using Maho.Syntax;

namespace Maho.Resolution;

internal sealed class GenericParameterSymbol : Symbol
{
    public Symbol GenericSymbol { get; }
    public GenericParameterKind ParameterKind { get; }
    public bool IsVariadic { get; }
    public GenericParameterSyntax? Syntax { get; }
    public TypeRef? DefaultType { get; internal set; }
    public bool HasSelfDefault { get; internal set; }

    public bool HasExplicitLayout { get; internal set; }
    public bool HasExplicitSized { get; internal set; }
    public bool HasExplicitUnsized { get; internal set; }

    public List<TypeRef> Constraints { get; internal set; }

    internal bool RequiresSized(ResolutionContext context)
    {
        if (HasExplicitSized)
            return true;

        if (HasExplicitLayout || HasExplicitUnsized)
            return ConstraintsRequireSized(context);

        return true;
    }

    internal bool RequiresUnsized(ResolutionContext context) =>
        !RequiresSized(context) && HasExplicitUnsized;

    internal bool RequiresCopy(ResolutionContext context)
    {
        if (!context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Copy, out var copyHandle))
            return false;

        var visited = new HashSet<SymbolHandle> { ResolutionContext.GetHandle(this) };
        if (ParameterKind == GenericParameterKind.This &&
            DeclarationResolutionPass.SatisfiesConstraint(context, ResolutionContext.GetHandle(GenericSymbol), copyHandle, visited))
        {
            return true;
        }

        foreach (var c in Constraints)
        {
            if (c.IsResolved && c.Handle is { } ch && DeclarationResolutionPass.SatisfiesConstraint(context, ch, copyHandle, visited))
                return true;
        }
        return false;
    }

    internal bool RequiresMoveOnly(ResolutionContext context)
    {
        if (RequiresCopy(context))
            return false;

        if (!context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Move, out var moveHandle))
            return false;

        var visited = new HashSet<SymbolHandle> { ResolutionContext.GetHandle(this) };
        if (ParameterKind == GenericParameterKind.This &&
            DeclarationResolutionPass.SatisfiesConstraint(context, ResolutionContext.GetHandle(GenericSymbol), moveHandle, visited))
        {
            return true;
        }

        foreach (var c in Constraints)
        {
            if (c.IsResolved && c.Handle is { } ch && DeclarationResolutionPass.SatisfiesConstraint(context, ch, moveHandle, visited))
                return true;
        }
        return false;
    }

    private bool ConstraintsRequireSized(ResolutionContext context)
    {
        if (!context.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Sized, out var sizedHandle))
            return false;

        foreach (var c in Constraints)
        {
            if (c.IsResolved && c.Handle is { } ch && DeclarationResolutionPass.SatisfiesConstraint(context, ch, sizedHandle, []))
                return true;
        }

        return false;
    }

    public GenericParameterSymbol(SymbolID id, Scope enclosingScope, SymbolPart name, Symbol genericSymbol, GenericParameterKind parameterKind, bool isVariadic, GenericParameterSyntax? syntax = null) : base(id, name, enclosingScope)
    {
        Kind = SymbolKind.GenericParameter;
        GenericSymbol = genericSymbol;
        ParameterKind = parameterKind;
        IsVariadic = isVariadic;
        Syntax = syntax;
        Constraints = [];
    }
}