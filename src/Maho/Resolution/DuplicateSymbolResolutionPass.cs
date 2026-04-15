using System.Collections.Generic;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Resolution;

/// <summary>
/// Reports duplicate declarations after earlier passes have resolved the declaration graph,
/// hierarchy data, and function signatures needed to judge partial declaration sets correctly.
/// </summary>
internal sealed class DuplicateSymbolResolutionPass : ResolutionPass
{
    /// <summary>
    /// Duplicate analysis reads the canonical project scope graph as a whole, so it runs entirely
    /// during project-wide finalization instead of doing unit-local work.
    /// </summary>
    public override void AfterProject(ResolutionCoordinatorContext context) => new ScopeWalker(context).Execute();

    /// <summary> Walks the canonical project scope tree and validates duplicate declaration rules. </summary>
    private sealed class ScopeWalker
    {
        private readonly ResolutionCoordinatorContext context;

        /// <summary> Creates the helper that validates one fully built project graph. </summary>
        public ScopeWalker(ResolutionCoordinatorContext context) => this.context = context;

        /// <summary> Starts duplicate analysis from the global scope. </summary>
        public void Execute() => VisitScope(context.GlobalScope);

        /// <summary> Validates one scope, then recursively visits every nested child scope. </summary>
        private void VisitScope(Scope scope)
        {
            ReportDuplicateTypes(scope);
            ReportDuplicateFunctions(scope);
            ReportDuplicateVariables(scope);
            ReportDuplicateProperties(scope);

            foreach (Scope child in scope.Children)
                VisitScope(child);
        }

        /// <summary> Reports duplicate type declarations when the declaration set is not fully partial. </summary>
        private void ReportDuplicateTypes(Scope scope)
        {
            Dictionary<TypeDeclarationKey, List<TypeSymbol>> groups = [];

            foreach (Symbol symbol in scope.DeclaredSymbols)
            {
                if (symbol is not TypeSymbol typeSymbol)
                    continue;

                if (!groups.TryGetValue(typeSymbol.DeclarationKey, out List<TypeSymbol>? group))
                {
                    group = [];
                    groups.Add(typeSymbol.DeclarationKey, group);
                }

                group.Add(typeSymbol);
            }

            foreach (List<TypeSymbol> group in groups.Values)
            {
                if (group.Count <= 1 || AllPartial(group))
                    continue;

                foreach (TypeSymbol typeSymbol in group)
                {
                    typeSymbol.MarkDuplicate();
                    context.Diagnostics.ReportDuplicateTypeDeclaration(
                        typeSymbol.Declaration.GetSpan() ?? default,
                        typeSymbol.Name.ToString(),
                        typeSymbol.Arity,
                        typeSymbol.Declaration.GetSource());
                }
            }
        }

        /// <summary>
        /// Reports duplicate function declarations when a set includes any non-partial declaration
        /// or when more than one partial declaration in the set provides a body.
        /// </summary>
        private void ReportDuplicateFunctions(Scope scope)
        {
            Dictionary<FunctionDeclarationKey, List<FunctionSymbol>> groups = [];

            foreach (Symbol symbol in scope.DeclaredSymbols)
            {
                if (symbol is not FunctionSymbol functionSymbol)
                    continue;

                FunctionDeclarationKey key = new(functionSymbol.Name, functionSymbol.Arity, functionSymbol.ParameterSignatureKey);

                if (!groups.TryGetValue(key, out List<FunctionSymbol>? group))
                {
                    group = [];
                    groups.Add(key, group);
                }

                group.Add(functionSymbol);
            }

            foreach (List<FunctionSymbol> group in groups.Values)
            {
                if (group.Count <= 1)
                    continue;

                bool hasNonPartial = false;
                int bodyCount = 0;

                foreach (FunctionSymbol functionSymbol in group)
                {
                    if (!IsPartial(functionSymbol))
                        hasNonPartial = true;

                    if (HasBody(functionSymbol))
                        bodyCount++;
                }

                if (!hasNonPartial && bodyCount <= 1)
                    continue;

                foreach (FunctionSymbol functionSymbol in group)
                {
                    functionSymbol.MarkDuplicate();
                    context.Diagnostics.ReportDuplicateFunctionDeclaration(
                        functionSymbol.Declaration.GetSpan() ?? default,
                        functionSymbol.Name.ToString(),
                        functionSymbol.Arity,
                        functionSymbol.Declaration.GetSource());
                }
            }
        }

        /// <summary> Reports duplicate variable declarations with the same name in one lexical scope. </summary>
        private void ReportDuplicateVariables(Scope scope)
        {
            Dictionary<SymbolName, List<VariableSymbol>> groups = [];

            foreach (Symbol symbol in scope.DeclaredSymbols)
            {
                if (symbol is not VariableSymbol variableSymbol)
                    continue;

                if (!groups.TryGetValue(variableSymbol.Name, out List<VariableSymbol>? group))
                {
                    group = [];
                    groups.Add(variableSymbol.Name, group);
                }

                group.Add(variableSymbol);
            }

            foreach (List<VariableSymbol> group in groups.Values)
            {
                if (group.Count <= 1)
                    continue;

                foreach (VariableSymbol variableSymbol in group)
                {
                    variableSymbol.MarkDuplicate();
                    context.Diagnostics.ReportDuplicateVariableDeclaration(
                        variableSymbol.Declaration.GetSpan() ?? default,
                        variableSymbol.Name.ToString(),
                        variableSymbol.Declaration.GetSource());
                }
            }
        }

        /// <summary> Reports duplicate property declarations with the same name in one type scope. </summary>
        private void ReportDuplicateProperties(Scope scope)
        {
            Dictionary<SymbolName, List<PropertySymbol>> groups = [];

            foreach (Symbol symbol in scope.DeclaredSymbols)
            {
                if (symbol is not PropertySymbol propertySymbol)
                    continue;

                if (!groups.TryGetValue(propertySymbol.Name, out List<PropertySymbol>? group))
                {
                    group = [];
                    groups.Add(propertySymbol.Name, group);
                }

                group.Add(propertySymbol);
            }

            foreach (List<PropertySymbol> group in groups.Values)
            {
                if (group.Count <= 1)
                    continue;

                foreach (PropertySymbol propertySymbol in group)
                {
                    propertySymbol.MarkDuplicate();
                    context.Diagnostics.ReportDuplicatePropertyDeclaration(
                        propertySymbol.Declaration.GetSpan() ?? default,
                        propertySymbol.Name.ToString(),
                        propertySymbol.Declaration.GetSource());
                }
            }
        }

        /// <summary> Tests whether every type declaration in the group carries the <c>partial</c> modifier. </summary>
        private static bool AllPartial(IReadOnlyList<TypeSymbol> types)
        {
            foreach (TypeSymbol typeSymbol in types)
            {
                if (!IsPartial(typeSymbol))
                    return false;
            }

            return true;
        }

        /// <summary> Tests whether one type declaration is marked <c>partial</c>. </summary>
        private static bool IsPartial(TypeSymbol typeSymbol) =>
            typeSymbol.Declaration is TypeDeclaration declaration && HasModifier(declaration.Modifiers, MatchingKeywordKind.Partial);

        /// <summary> Tests whether one function declaration is marked <c>partial</c>. </summary>
        private static bool IsPartial(FunctionSymbol functionSymbol) =>
            functionSymbol.Declaration is FunctionDeclaration declaration && HasModifier(declaration.Signature.Modifiers, MatchingKeywordKind.Partial);

        /// <summary> Tests whether one function declaration contributes an implementation body. </summary>
        private static bool HasBody(FunctionSymbol functionSymbol) =>
            functionSymbol.Declaration is FunctionDeclaration declaration && declaration.Body is not FunctionEmptyBody;

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
}
