using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Symbols;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class ResolutionTests
{
    [Fact]
    public void Resolve_SymbolDiscovery_CreatesDeclarationSymbolsAndScopes()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            namespace Demo;

            public class Box<T>
            {
                public T Value;
                public class Nested;

                public static int Make(int argc)
                {
                    public class LocalBox;
                    public static int LocalFunc()
                    {
                        return 0;
                    }

                    int value;
                    return 0;
                }
            }

            public static int Main(int argc)
            {
                int value;
                return 0;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionResult result = CompilerTestBed.ResolveProject(root).Units[0];
        TopLevelTypeDeclaration typeWrapper = Assert.IsType<TopLevelTypeDeclaration>(root.Members[1]);
        TopLevelFunctionDeclaration functionWrapper = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[2]);
        TypeBlockBody typeBody = Assert.IsType<TypeBlockBody>(typeWrapper.Type.Body);
        MemberFieldDeclaration field = Assert.IsType<MemberFieldDeclaration>(typeBody.Members[0]);
        MemberTypeDeclaration nestedType = Assert.IsType<MemberTypeDeclaration>(typeBody.Members[1]);
        MemberFunctionDeclaration memberFunction = Assert.IsType<MemberFunctionDeclaration>(typeBody.Members[2]);
        FunctionBlockBody memberFunctionBody = Assert.IsType<FunctionBlockBody>(memberFunction.Function.Body);
        LocalTypeDeclaration localType = Assert.IsType<LocalTypeDeclaration>(memberFunctionBody.Locals[0]);
        LocalFunctionDeclaration localFunction = Assert.IsType<LocalFunctionDeclaration>(memberFunctionBody.Locals[1]);
        Assert.IsType<LocalVariableDeclarationStatement>(memberFunctionBody.Locals[2]);

        Assert.True(result.TryResolveDeclaredSymbol(typeWrapper.Type, out Symbol? typeSymbol));
        Assert.True(result.TryResolveDeclaredSymbol(functionWrapper.Function, out Symbol? functionSymbol));
        Assert.True(result.TryResolveDeclaredSymbol(nestedType.Type, out Symbol? nestedTypeSymbol));
        Assert.True(result.TryResolveDeclaredSymbol(memberFunction.Function, out Symbol? memberFunctionSymbol));
        Assert.True(result.TryResolveDeclaredSymbol(localType.Type, out Symbol? localTypeSymbol));
        Assert.True(result.TryResolveDeclaredSymbol(localFunction.Function, out Symbol? localFunctionSymbol));

        TypeSymbol resolvedType = Assert.IsType<TypeSymbol>(typeSymbol);
        FunctionSymbol resolvedFunction = Assert.IsType<FunctionSymbol>(functionSymbol);
        Assert.IsType<TypeSymbol>(nestedTypeSymbol);
        Assert.IsType<FunctionSymbol>(memberFunctionSymbol);
        Assert.IsType<TypeSymbol>(localTypeSymbol);
        Assert.IsType<FunctionSymbol>(localFunctionSymbol);

        Assert.Equal(1, resolvedType.Arity);
        Assert.Single(resolvedType.TypeParameters);
        Assert.Equal(1, resolvedFunction.ParameterCount);
        Assert.Equal(5, result.TypeReferences.Count);

        Assert.True(result.TryResolveScope(typeWrapper.Type.Body, out Scope? typeScope));
        Assert.NotNull(typeScope);
        Assert.Contains(typeScope.DeclaredSymbols, symbol => symbol is TypeSymbol type && type.Name.ToString() == "Nested");
        Assert.Contains(typeScope.DeclaredSymbols, symbol => symbol is FunctionSymbol function && function.Name.ToString() == "Make");

        Assert.True(result.TryResolveScope(functionWrapper.Function.Body, out Scope? functionScope));
        Assert.NotNull(functionScope);
        Assert.Contains(functionScope.DeclaredSymbols, symbol => symbol is ParameterSymbol parameter && parameter.Name.ToString() == "argc");
        Assert.Contains(functionScope.DeclaredSymbols, symbol => symbol is VariableSymbol variable && variable.Name.ToString() == "value");
    }

    [Fact]
    public void Resolve_SymbolDiscovery_MergesSharedNamespaceScopeAcrossUnits()
    {
        var (_, diagnostics1, _, root1) = CompilerTestBed.Parse("""
            namespace Demo;
            public class Foo;
            """);
        var (_, diagnostics2, _, root2) = CompilerTestBed.Parse("""
            namespace Demo;
            public class Bar;
            """);

        Assert.Empty(diagnostics1.Diagnostics);
        Assert.Empty(diagnostics2.Diagnostics);

        ResolutionProjectResult result = CompilerTestBed.ResolveProject(root1, root2);

        IReadOnlyList<Symbol> demoNamespaceSymbols = result.GlobalScope.LookupLocal(SymbolName.FromLiteral("Demo"));
        NamespaceSymbol demoNamespace = Assert.IsType<NamespaceSymbol>(Assert.Single(demoNamespaceSymbols));
        Scope namespaceScope = result.SymbolScopes[demoNamespace];

        Assert.Contains(namespaceScope.DeclaredSymbols, symbol => symbol is TypeSymbol type && type.Name.ToString() == "Foo");
        Assert.Contains(namespaceScope.DeclaredSymbols, symbol => symbol is TypeSymbol type && type.Name.ToString() == "Bar");
        Assert.Equal(2, result.Units.Length);
    }

    [Fact]
    public void Resolve_SymbolDiscovery_AssociatesBaseClausesAndConstraintClausesWithDeclaredSymbols()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public class Box<T> : Base where T: Constraint
            {
                public static TResult Build<TResult>() where TResult: Output;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionResult result = CompilerTestBed.ResolveProject(root).Units[0];
        TopLevelTypeDeclaration typeWrapper = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(root.Members));
        TypeDeclaration typeDeclaration = typeWrapper.Type;
        TypeConstraintClause typeConstraintClause = Assert.Single(typeDeclaration.Constraints);
        GenericName typeName = Assert.IsType<GenericName>(typeDeclaration.Name);

        TypeBlockBody typeBody = Assert.IsType<TypeBlockBody>(typeDeclaration.Body);
        MemberFunctionDeclaration functionMember = Assert.IsType<MemberFunctionDeclaration>(Assert.Single(typeBody.Members));
        FunctionDeclaration functionDeclaration = functionMember.Function;
        TypeConstraintClause functionConstraintClause = Assert.Single(functionDeclaration.Signature.Constraints);
        GenericName functionName = Assert.IsType<GenericName>(functionDeclaration.Signature.Identifier);

        Assert.True(result.TryResolveDeclaredSymbol(typeDeclaration, out Symbol? typeSymbol));
        Assert.True(result.TryResolveDeclaredSymbol(functionDeclaration, out Symbol? functionSymbol));
        Assert.True(result.TryResolveDeclaredSymbol(typeName.TypeParameters[0], out Symbol? declaredTypeParameterSymbol));
        Assert.True(result.TryResolveDeclaredSymbol(functionName.TypeParameters[0], out Symbol? declaredFunctionTypeParameterSymbol));

        Assert.True(result.TryResolveDeclaredSymbol(typeDeclaration.Base!, out Symbol? resolvedBaseClauseSymbol));
        Assert.Same(typeSymbol, resolvedBaseClauseSymbol);

        Assert.True(result.TryResolveDeclaredSymbol(typeConstraintClause, out Symbol? resolvedTypeConstraintSymbol));
        Assert.Same(typeSymbol, resolvedTypeConstraintSymbol);

        Assert.True(result.TryResolveDeclaredSymbol(typeConstraintClause.TypeParameter, out Symbol? resolvedTypeConstraintTypeParameterSymbol));
        Assert.Same(declaredTypeParameterSymbol, resolvedTypeConstraintTypeParameterSymbol);

        Assert.True(result.TryResolveDeclaredSymbol(functionConstraintClause, out Symbol? resolvedFunctionConstraintSymbol));
        Assert.Same(functionSymbol, resolvedFunctionConstraintSymbol);

        Assert.True(result.TryResolveDeclaredSymbol(functionConstraintClause.TypeParameter, out Symbol? resolvedFunctionConstraintTypeParameterSymbol));
        Assert.Same(declaredFunctionTypeParameterSymbol, resolvedFunctionConstraintTypeParameterSymbol);
    }

    [Fact]
    public void Resolve_TypeHierarchy_ResolvesDirectBaseTypesAcrossUnits()
    {
        var (_, diagnostics1, _, root1) = CompilerTestBed.Parse("""
            namespace Demo;
            public class Base;
            """);
        var (_, diagnostics2, _, root2) = CompilerTestBed.Parse("""
            namespace Demo;
            public class Derived : Base;
            """);

        Assert.Empty(diagnostics1.Diagnostics);
        Assert.Empty(diagnostics2.Diagnostics);

        ResolutionProjectResult result = CompilerTestBed.ResolveProject(root1, root2);
        ResolutionResult derivedUnit = result.Units[1];
        TopLevelTypeDeclaration derivedWrapper = Assert.IsType<TopLevelTypeDeclaration>(root2.Members[1]);
        TypeDeclaration derivedDeclaration = derivedWrapper.Type;

        Assert.True(derivedUnit.TryResolveDeclaredSymbol(derivedDeclaration, out Symbol? declaredSymbol));
        TypeSymbol derivedType = Assert.IsType<TypeSymbol>(declaredSymbol);
        TypeSymbol resolvedBaseType = Assert.IsType<TypeSymbol>(Assert.Single(derivedType.BaseTypes));

        Assert.Equal("Base", resolvedBaseType.Name.ToString());
        Assert.True(derivedUnit.TryResolveTypeReference(derivedDeclaration.Base!.BaseTypes[0], out ResolvedTypeReference? resolvedReference));
        Assert.Same(resolvedBaseType, Assert.IsType<TypeSymbol>(Assert.Single([.. resolvedReference!.CandidateSymbols])));
    }

    [Fact]
    public void Resolve_TypeHierarchy_ReportsCycleDiagnosticsDuringFinalization()
    {
        var (_, parseDiagnostics, _, root) = CompilerTestBed.Parse("""
            public class A : B;
            public class B : A;
            """);

        Assert.Empty(parseDiagnostics.Diagnostics);

        DiagnosticsManager diagnostics = new();
        Resolver resolver = new(diagnostics);
        _ = resolver.Resolve(SyntaxTree.CreateSingleRoot(root));

        Diagnostic[] cycleDiagnostics = [.. diagnostics.Diagnostics.Where(static diagnostic => diagnostic.DiagnosticCode == "MH1004")];
        Assert.Equal(2, cycleDiagnostics.Length);
        Assert.Contains(cycleDiagnostics, diagnostic => diagnostic.Message.Contains("'A'"));
        Assert.Contains(cycleDiagnostics, diagnostic => diagnostic.Message.Contains("'B'"));
    }

    [Fact]
    public void Resolve_FunctionSignature_ResolvesReturnAndParameterTypes()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            namespace Demo;

            public class Input;
            public class Output;
            public static Output Make(Input value);
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionResult result = CompilerTestBed.ResolveProject(root).Units[0];
        TopLevelFunctionDeclaration functionWrapper = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[3]);
        FunctionDeclaration declaration = functionWrapper.Function;

        Assert.True(result.TryResolveDeclaredSymbol(declaration, out Symbol? declaredSymbol));
        FunctionSymbol function = Assert.IsType<FunctionSymbol>(declaredSymbol);
        Assert.NotNull(function.ReturnType);
        Assert.Equal("Demo.Output", function.ReturnType!.SignatureKey);
        Assert.Single(function.Parameters);
        Assert.NotNull(function.Parameters[0].Type);
        Assert.Equal("Demo.Input", function.Parameters[0].Type!.SignatureKey);

        Assert.True(result.TryResolveTypeReference(declaration.Signature.ReturnType, out ResolvedTypeReference? resolvedReturnType));
        Assert.Equal("Demo.Output", resolvedReturnType!.SignatureKey);
    }

    [Fact]
    public void Resolve_FunctionSignature_AllowsDeferredVarReturnAndDynParameter()
    {
        var (_, parseDiagnostics, _, root) = CompilerTestBed.Parse("""
            public static var Make(dyn value);
            """);

        Assert.Empty(parseDiagnostics.Diagnostics);

        DiagnosticsManager diagnostics = new();
        ResolutionResult result = new Resolver(diagnostics).Resolve(root);
        TopLevelFunctionDeclaration functionWrapper = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        FunctionDeclaration declaration = functionWrapper.Function;

        Assert.True(result.TryResolveDeclaredSymbol(declaration, out Symbol? declaredSymbol));
        FunctionSymbol function = Assert.IsType<FunctionSymbol>(declaredSymbol);
        Assert.Equal("var", function.ReturnType!.SignatureKey);
        Assert.Equal("dyn", function.Parameters[0].Type!.SignatureKey);
        Assert.Empty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Resolve_FunctionSignature_ParameterVarRequiresRealTypeNamedVar()
    {
        var (_, parseDiagnostics, _, root) = CompilerTestBed.Parse("""
            public static dyn Make(var value);
            """);

        Assert.Empty(parseDiagnostics.Diagnostics);

        DiagnosticsManager diagnostics = new();
        ResolutionResult result = new Resolver(diagnostics).Resolve(root);
        TopLevelFunctionDeclaration functionWrapper = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        FunctionDeclaration declaration = functionWrapper.Function;

        Assert.True(result.TryResolveDeclaredSymbol(declaration, out Symbol? declaredSymbol));
        FunctionSymbol function = Assert.IsType<FunctionSymbol>(declaredSymbol);
        Assert.Equal("var", function.Parameters[0].Type!.SignatureKey);
        Assert.Contains(diagnostics.Diagnostics, diagnostic => diagnostic.DiagnosticCode == "MH1000" && diagnostic.Message.Contains("'var'"));
    }

    [Fact]
    public void Resolve_FunctionSignature_PrimitiveLikeNamesStillRequireScopeLookup()
    {
        var (_, parseDiagnostics, _, root) = CompilerTestBed.Parse("""
            public static int Make(int value);
            """);

        Assert.Empty(parseDiagnostics.Diagnostics);

        DiagnosticsManager diagnostics = new();
        ResolutionResult result = new Resolver(diagnostics).Resolve(root);
        TopLevelFunctionDeclaration functionWrapper = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        FunctionDeclaration declaration = functionWrapper.Function;

        Assert.True(result.TryResolveDeclaredSymbol(declaration, out Symbol? declaredSymbol));
        FunctionSymbol function = Assert.IsType<FunctionSymbol>(declaredSymbol);
        Assert.Equal("int", function.ReturnType!.SignatureKey);
        Assert.Equal("int", function.Parameters[0].Type!.SignatureKey);
        Assert.Equal(2, diagnostics.Diagnostics.Count(diagnostic => diagnostic.DiagnosticCode == "MH1000"));
        Assert.All(
            diagnostics.Diagnostics.Where(diagnostic => diagnostic.DiagnosticCode == "MH1000"),
            diagnostic => Assert.Contains("'int'", diagnostic.Message));
    }

    [Fact]
    public void Resolve_FunctionSignature_QualifiedVarAndDynUseNormalTypeLookup()
    {
        var (_, parseDiagnostics, _, root) = CompilerTestBed.Parse("""
            namespace Builtins;
            public class var;
            public class dyn;

            public static Builtins.var Make(Builtins.dyn value);
            """);

        Assert.Empty(parseDiagnostics.Diagnostics);

        DiagnosticsManager diagnostics = new();
        ResolutionResult result = new Resolver(diagnostics).Resolve(root);
        TopLevelFunctionDeclaration functionWrapper = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[3]);
        FunctionDeclaration declaration = functionWrapper.Function;

        Assert.True(result.TryResolveDeclaredSymbol(declaration, out Symbol? declaredSymbol));
        FunctionSymbol function = Assert.IsType<FunctionSymbol>(declaredSymbol);
        Assert.Equal("Builtins.var", function.ReturnType!.SignatureKey);
        Assert.Equal("Builtins.dyn", function.Parameters[0].Type!.SignatureKey);
        Assert.Empty(diagnostics.Diagnostics);
    }

    [Fact]
    public void Resolve_DuplicateSymbols_ReportsNonPartialTypesAndAllowsFullyPartialTypes()
    {
        var (_, parseDiagnostics, _, root) = CompilerTestBed.Parse("""
            public class Box;
            public class Box;

            public partial class PartialBox;
            public partial class PartialBox;
            """);

        Assert.Empty(parseDiagnostics.Diagnostics);

        DiagnosticsManager diagnostics = new();
        ResolutionProjectResult result = new Resolver(diagnostics).ResolveProject(new ResolutionProject(SyntaxTree.CreateSingleRoot(root)));

        Diagnostic[] duplicateTypeDiagnostics = [.. diagnostics.Diagnostics.Where(static diagnostic => diagnostic.DiagnosticCode == "MH1002")];
        Assert.Equal(2, duplicateTypeDiagnostics.Length);

        TopLevelTypeDeclaration firstBox = Assert.IsType<TopLevelTypeDeclaration>(root.Members[0]);
        TopLevelTypeDeclaration secondBox = Assert.IsType<TopLevelTypeDeclaration>(root.Members[1]);
        TopLevelTypeDeclaration firstPartialBox = Assert.IsType<TopLevelTypeDeclaration>(root.Members[2]);
        TopLevelTypeDeclaration secondPartialBox = Assert.IsType<TopLevelTypeDeclaration>(root.Members[3]);

        TypeSymbol duplicateBox1 = Assert.IsType<TypeSymbol>(result.Units[0].DeclaredSymbols[firstBox.Type]);
        TypeSymbol duplicateBox2 = Assert.IsType<TypeSymbol>(result.Units[0].DeclaredSymbols[secondBox.Type]);
        TypeSymbol partialBox1 = Assert.IsType<TypeSymbol>(result.Units[0].DeclaredSymbols[firstPartialBox.Type]);
        TypeSymbol partialBox2 = Assert.IsType<TypeSymbol>(result.Units[0].DeclaredSymbols[secondPartialBox.Type]);

        Assert.True(duplicateBox1.IsDuplicate);
        Assert.True(duplicateBox2.IsDuplicate);
        Assert.False(partialBox1.IsDuplicate);
        Assert.False(partialBox2.IsDuplicate);
    }

    [Fact]
    public void Resolve_DuplicateSymbols_ReportsInvalidFunctionSetsAndAllowsSinglePartialBody()
    {
        var (_, parseDiagnostics, _, root) = CompilerTestBed.Parse("""
            public class Result;
            public class Input;

            public static Result Make(Input value);
            public static Result Make(Input value) { }

            public partial static Result Merge(Input value);
            public partial static Result Merge(Input value) { }

            public partial static Result Build(Input value) { }
            public partial static Result Build(Input value) { }
            """);

        Assert.Empty(parseDiagnostics.Diagnostics);

        DiagnosticsManager diagnostics = new();
        ResolutionProjectResult result = new Resolver(diagnostics).ResolveProject(new ResolutionProject(SyntaxTree.CreateSingleRoot(root)));

        Diagnostic[] duplicateFunctionDiagnostics = [.. diagnostics.Diagnostics.Where(static diagnostic => diagnostic.DiagnosticCode == "MH1003")];
        Assert.Equal(4, duplicateFunctionDiagnostics.Length);

        TopLevelFunctionDeclaration make1 = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[2]);
        TopLevelFunctionDeclaration make2 = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[3]);
        TopLevelFunctionDeclaration merge1 = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[4]);
        TopLevelFunctionDeclaration merge2 = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[5]);
        TopLevelFunctionDeclaration build1 = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[6]);
        TopLevelFunctionDeclaration build2 = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[7]);

        FunctionSymbol duplicateMake1 = Assert.IsType<FunctionSymbol>(result.Units[0].DeclaredSymbols[make1.Function]);
        FunctionSymbol duplicateMake2 = Assert.IsType<FunctionSymbol>(result.Units[0].DeclaredSymbols[make2.Function]);
        FunctionSymbol partialMerge1 = Assert.IsType<FunctionSymbol>(result.Units[0].DeclaredSymbols[merge1.Function]);
        FunctionSymbol partialMerge2 = Assert.IsType<FunctionSymbol>(result.Units[0].DeclaredSymbols[merge2.Function]);
        FunctionSymbol duplicateBuild1 = Assert.IsType<FunctionSymbol>(result.Units[0].DeclaredSymbols[build1.Function]);
        FunctionSymbol duplicateBuild2 = Assert.IsType<FunctionSymbol>(result.Units[0].DeclaredSymbols[build2.Function]);

        Assert.True(duplicateMake1.IsDuplicate);
        Assert.True(duplicateMake2.IsDuplicate);
        Assert.False(partialMerge1.IsDuplicate);
        Assert.False(partialMerge2.IsDuplicate);
        Assert.True(duplicateBuild1.IsDuplicate);
        Assert.True(duplicateBuild2.IsDuplicate);
    }

    [Fact]
    public void Resolve_DuplicateSymbols_ReportsDuplicateVariablesAndProperties()
    {
        var (_, parseDiagnostics, _, root) = CompilerTestBed.Parse("""
            public class Box
            {
                public dyn Value { get; }
                public dyn Value { get; }
            }

            public static dyn Main()
            {
                int value;
                int value;
            }
            """);

        Assert.Empty(parseDiagnostics.Diagnostics);

        DiagnosticsManager diagnostics = new();
        ResolutionProjectResult result = new Resolver(diagnostics).ResolveProject(new ResolutionProject(SyntaxTree.CreateSingleRoot(root)));

        Diagnostic[] duplicateVariableDiagnostics = [.. diagnostics.Diagnostics.Where(static diagnostic => diagnostic.DiagnosticCode == "MH1005")];
        Diagnostic[] duplicatePropertyDiagnostics = [.. diagnostics.Diagnostics.Where(static diagnostic => diagnostic.DiagnosticCode == "MH1006")];
        Assert.Equal(2, duplicateVariableDiagnostics.Length);
        Assert.Equal(2, duplicatePropertyDiagnostics.Length);

        TopLevelTypeDeclaration typeWrapper = Assert.IsType<TopLevelTypeDeclaration>(root.Members[0]);
        TypeBlockBody typeBody = Assert.IsType<TypeBlockBody>(typeWrapper.Type.Body);
        MemberPropertyDeclaration property1 = Assert.IsType<MemberPropertyDeclaration>(typeBody.Members[0]);
        MemberPropertyDeclaration property2 = Assert.IsType<MemberPropertyDeclaration>(typeBody.Members[1]);

        TopLevelFunctionDeclaration functionWrapper = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[1]);
        FunctionBlockBody functionBody = Assert.IsType<FunctionBlockBody>(functionWrapper.Function.Body);
        LocalVariableDeclarationStatement variable1 = Assert.IsType<LocalVariableDeclarationStatement>(functionBody.Locals[0]);
        LocalVariableDeclarationStatement variable2 = Assert.IsType<LocalVariableDeclarationStatement>(functionBody.Locals[1]);

        PropertySymbol duplicateProperty1 = Assert.IsType<PropertySymbol>(result.Units[0].DeclaredSymbols[property1]);
        PropertySymbol duplicateProperty2 = Assert.IsType<PropertySymbol>(result.Units[0].DeclaredSymbols[property2]);
        VariableSymbol duplicateVariable1 = Assert.IsType<VariableSymbol>(result.Units[0].DeclaredSymbols[variable1.Declaration.Declarators[0]]);
        VariableSymbol duplicateVariable2 = Assert.IsType<VariableSymbol>(result.Units[0].DeclaredSymbols[variable2.Declaration.Declarators[0]]);

        Assert.True(duplicateProperty1.IsDuplicate);
        Assert.True(duplicateProperty2.IsDuplicate);
        Assert.True(duplicateVariable1.IsDuplicate);
        Assert.True(duplicateVariable2.IsDuplicate);
    }

    [Fact]
    public void Resolve_IntrinsicAttributes_RecordsRecognizedDefinitionsFromAnyNamespace()
    {
        var (_, parseDiagnostics1, _, root1) = CompilerTestBed.Parse("""
            namespace Outer
            {
                public intrinsic attribute Intrinsic;
            }
            """);
        var (_, parseDiagnostics2, _, root2) = CompilerTestBed.Parse("""
            namespace Other;
            public intrinsic attribute Intrinsic;
            """);

        Assert.Empty(parseDiagnostics1.Diagnostics);
        Assert.Empty(parseDiagnostics2.Diagnostics);

        DiagnosticsManager diagnostics = new();
        ResolutionProjectResult result = new Resolver(diagnostics).ResolveProject(new ResolutionProject(new SyntaxTree("test-project", [root1, root2])));

        Assert.True(result.IntrinsicAttributeDefinitions.TryGetValue("Intrinsic", out TypeSymbol[]? intrinsicDefinitions));
        Assert.Equal(2, intrinsicDefinitions.Length);
        Assert.All(intrinsicDefinitions, symbol => Assert.Equal(TypeKind.Attribute, Assert.IsType<TypeDeclaration>(symbol.Declaration).Kind));
        Assert.DoesNotContain(diagnostics.Diagnostics, diagnostic => diagnostic.DiagnosticCode == "MH1007");
        Assert.DoesNotContain(diagnostics.Diagnostics, diagnostic => diagnostic.DiagnosticCode == "MH1008");
    }

    [Fact]
    public void Resolve_IntrinsicAttributes_ReportsUnknownAndMissingSymbolsExhaustively()
    {
        var (_, parseDiagnostics, _, root) = CompilerTestBed.Parse("""
            namespace Demo;
            public intrinsic attribute Unknown;
            public attribute Intrinsic;
            """);

        Assert.Empty(parseDiagnostics.Diagnostics);

        DiagnosticsManager diagnostics = new();
        ResolutionProjectResult result = new Resolver(diagnostics).ResolveProject(new ResolutionProject(SyntaxTree.CreateSingleRoot(root)));

        Assert.True(result.IntrinsicAttributeDefinitions.TryGetValue("Unknown", out TypeSymbol[]? unknownDefinitions));
        Assert.Single(unknownDefinitions);
        Assert.False(result.IntrinsicAttributeDefinitions.ContainsKey("Intrinsic"));

        Diagnostic unknownDiagnostic = Assert.Single(diagnostics.Diagnostics, static diagnostic => diagnostic.DiagnosticCode == "MH1007");
        Assert.Contains("'Unknown'", unknownDiagnostic.Message);

        Diagnostic missingDiagnostic = Assert.Single(diagnostics.Diagnostics, static diagnostic => diagnostic.DiagnosticCode == "MH1008");
        Assert.Contains("'Intrinsic'", missingDiagnostic.Message);
    }
}
