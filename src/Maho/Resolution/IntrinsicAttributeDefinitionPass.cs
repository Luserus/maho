using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Resolution;

/// <summary>
/// Collects compiler-known intrinsic attribute declarations after duplicate analysis has already
/// marked invalid declaration sets, then reports unknown and missing intrinsic attribute symbols.
/// </summary>
internal sealed class IntrinsicAttributeDefinitionPass : ResolutionPass
{
    /// <summary> Supported intrinsic attribute declaration names keyed by their simple compiler-known symbol text. </summary>
    private static readonly string[] SupportedIntrinsicAttributes = ["Intrinsic"];

    /// <summary>
    /// Intrinsic attribute discovery reads the canonical project graph as a whole, records every
    /// intrinsic-marked attribute declaration, then finalizes by reporting unknown and missing symbols.
    /// </summary>
    public override void Execute(ResolutionCoordinatorContext context)
    {
        Collect(context, context.GlobalScope);
        Finalize(context);
    }

    /// <summary> Reports project-wide diagnostics after all intrinsic definitions have been recorded. </summary>
    private void Finalize(ResolutionCoordinatorContext context)
    {
        ReportUnrecognizedIntrinsicAttributes(context);
        ReportUndeclaredIntrinsicAttributes(context);
    }

    /// <summary> Walks one scope and records every intrinsic-marked attribute declaration found there. </summary>
    private static void Collect(ResolutionCoordinatorContext context, Scope scope)
    {
        foreach (Symbol symbol in scope.DeclaredSymbols)
        {
            if (symbol is not TypeSymbol typeSymbol || typeSymbol.Declaration is not TypeDeclaration declaration)
                continue;

            if (declaration.Kind is not TypeKind.Attribute || !HasModifier(declaration.Modifiers, MatchingKeywordKind.Intrinsic))
                continue;

            context.RecordIntrinsicAttributeDefinition(typeSymbol.Name.ToString(), typeSymbol);
        }

        foreach (Scope child in scope.Children)
            Collect(context, child);
    }

    /// <summary> Reports every non-duplicate intrinsic-marked attribute whose name is not compiler-recognized. </summary>
    private static void ReportUnrecognizedIntrinsicAttributes(ResolutionCoordinatorContext context)
    {
        foreach ((string name, List<TypeSymbol> definitions) in context.IntrinsicAttributeDefinitions)
        {
            if (IsSupportedIntrinsicAttribute(name))
                continue;

            foreach (TypeSymbol definition in definitions)
            {
                if (definition.IsDuplicate)
                    continue;

                context.Diagnostics.ReportUnrecognizedIntrinsicAttribute(
                    definition.Declaration.GetSpan() ?? default,
                    name,
                    definition.Declaration.GetSource());
            }
        }
    }

    /// <summary> Reports every supported intrinsic attribute name that has no non-duplicate declaration anywhere in the project. </summary>
    private void ReportUndeclaredIntrinsicAttributes(ResolutionCoordinatorContext context)
    {
        if (context.IntrinsicAttributeDefinitions.Count == 0)
            return;

        foreach (string supportedName in SupportedIntrinsicAttributes)
        {
            if (HasNonDuplicateDefinition(context, supportedName))
                continue;

            (TextSpan span, SourceText? source) = GetProjectDiagnosticAnchor(context);
            context.Diagnostics.ReportUndeclaredIntrinsicAttribute(span, supportedName, source);
        }
    }

    /// <summary> Tests whether one compiler-known intrinsic attribute has at least one non-duplicate declaration. </summary>
    private static bool HasNonDuplicateDefinition(ResolutionCoordinatorContext context, string name)
    {
        if (!context.IntrinsicAttributeDefinitions.TryGetValue(name, out List<TypeSymbol>? definitions))
            return false;

        foreach (TypeSymbol definition in definitions)
        {
            if (!definition.IsDuplicate)
                return true;
        }

        return false;
    }

    /// <summary> Picks a stable source anchor for project-wide diagnostics that are not tied to one declaration site. </summary>
    private static (TextSpan Span, SourceText? Source) GetProjectDiagnosticAnchor(ResolutionCoordinatorContext context)
    {
        if (context.Units.Length == 0)
            return (default, null);

        CompilationUnit root = context.Units[0].Root;
        return (root.GetSpan() ?? default, root.GetSource());
    }

    /// <summary> Tests whether a simple intrinsic attribute name is currently compiler-supported. </summary>
    private static bool IsSupportedIntrinsicAttribute(string name)
    {
        foreach (string supported in SupportedIntrinsicAttributes)
        {
            if (supported == name)
                return true;
        }

        return false;
    }

    /// <summary> Tests whether a modifier list contains the requested contextual keyword. </summary>
    private static bool HasModifier(IReadOnlyList<Token> modifiers, MatchingKeywordKind kind)
    {
        foreach (Token modifier in modifiers)
        {
            if (modifier.MatchingKind == kind)
                return true;
        }

        return false;
    }
}
