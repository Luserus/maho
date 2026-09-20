using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class DeclarationResolutionTests
{
    [Fact]
    public void Resolve_ResolvesDeclarationTypesAndNestedMembers()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Value;
            public struct Container : Value
            {
                public Value field;
                public Value Property { get; }
                public Value Method(Value parameter)
                {
                    Value local = parameter;
                    return local;
                }
            }
            public Value Function(Value parameter) { return parameter; }
            public Value global;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        TypeSymbol value = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Value");
        var valueHandle = ResolutionContext.GetHandle(value);
        ProductTypeSymbol container = Assert.IsType<ProductTypeSymbol>(Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Container"));

        Assert.Equal(TypeRef.Resolved(valueHandle), Assert.Single(container.BaseTypes));
        Assert.Equal(TypeRef.Resolved(valueHandle), Assert.Single(context.FieldSymbols).Type);
        Assert.Equal(TypeRef.Resolved(valueHandle), Assert.Single(context.PropertySymbols).Type);
        Assert.Equal(TypeRef.Resolved(valueHandle), Assert.Single(context.MethodSymbols).ReturnType);
        Assert.Equal(TypeRef.Resolved(valueHandle), Assert.Single(context.FunctionSymbols).ReturnType);
        Assert.Equal(TypeRef.Resolved(valueHandle), Assert.Single(context.GlobalVariableSymbols).Type);
        Assert.All(context.ParameterSymbols, parameter => Assert.Equal(TypeRef.Resolved(valueHandle), parameter.Type));
        Assert.Equal(TypeRef.Resolved(valueHandle), Assert.Single(context.LocalVariableSymbols).Type);
        Assert.Single(container.Fields);
        Assert.Single(container.Properties);
        Assert.Single(container.Methods);

        TopLevelTypeDeclaration containerSyntax = Assert.IsType<TopLevelTypeDeclaration>(root.Members[1]);
        TypeBlockBody body = Assert.IsType<TypeBlockBody>(containerSyntax.Type.Body);
        MemberFieldDeclaration field = Assert.IsType<MemberFieldDeclaration>(body.Members[0]);
        MemberPropertyDeclaration property = Assert.IsType<MemberPropertyDeclaration>(body.Members[1]);
        MemberFunctionDeclaration method = Assert.IsType<MemberFunctionDeclaration>(body.Members[2]);
        FunctionBlockBody methodBody = Assert.IsType<FunctionBlockBody>(method.Function.Body);
        LocalVariableDeclarationStatement local = Assert.IsType<LocalVariableDeclarationStatement>(methodBody.Locals[0]);
        TopLevelFunctionDeclaration function = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[2]);
        TopLevelVariableDeclaration global = Assert.IsType<TopLevelVariableDeclaration>(root.Members[3]);

        AssertReference(context, Assert.Single(containerSyntax.Type.Base!.BaseTypes), valueHandle);
        AssertReference(context, field.Declaration.Type, valueHandle);
        AssertReference(context, property.Type, valueHandle);
        AssertReference(context, method.Function.Signature.ReturnType, valueHandle);
        AssertReference(context, Assert.Single(method.Function.Signature.Parameters).Declarator.Type, valueHandle);
        AssertReference(context, local.Declaration.Type, valueHandle);
        AssertReference(context, function.Function.Signature.ReturnType, valueHandle);
        AssertReference(context, Assert.Single(function.Function.Signature.Parameters).Declarator.Type, valueHandle);
        AssertReference(context, global.Declaration.Type, valueHandle);
    }

    [Fact]
    public void Resolve_ResolvesAliasesAndAliasTypeReferences()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            namespace Namespace
            {
                public struct Constraint;
                public struct Int32 : Constraint;
                public struct Plain;
                public class Type<T, U>;
                public class Generic<T> where T : Constraint;
            }

            using Direct = Namespace.Plain;
            using Specialized = Namespace.Generic<Namespace.Int32>;
            using Projected<T> where T : Namespace.Constraint = Namespace.Generic<T>;
            using Invalid<T> = Namespace.Generic<T>;
            using Mixed<T> = Namespace.Type<T, Int32>;

            public Direct direct;
            public Specialized specialized;
            public Projected<Namespace.Int32> projected;
            public Mixed<Int32> mixed;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        AliasSymbol direct = Assert.Single(context.AliasSymbols, symbol => symbol.Name.ToString() == "Direct");
        AliasSymbol specialized = Assert.Single(context.AliasSymbols, symbol => symbol.Name.ToString() == "Specialized");
        AliasSymbol projected = Assert.Single(context.AliasSymbols, symbol => symbol.Name.ToString() == "Projected");
        AliasSymbol invalid = Assert.Single(context.AliasSymbols, symbol => symbol.Name.ToString() == "Invalid");
        AliasSymbol mixed = Assert.Single(context.AliasSymbols, symbol => symbol.Name.ToString() == "Mixed");
        TypeSymbol plain = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Plain");
        TypeSymbol type = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Type");
        TypeSymbol generic = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Generic");
        TypeSymbol constraint = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Constraint");

        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(plain)), direct.Target);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(generic)), specialized.Target);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(generic)), projected.Target);
        Assert.True(projected.HasCompatibleConstraints);
        Assert.Single(projected.GenericParameters);
        GenericParameterSymbol projectedParameter = context.GenericParameterSymbols[projected.GenericParameters[0].ID];
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(constraint)), Assert.Single(projectedParameter.Constraints));
        Assert.False(invalid.HasCompatibleConstraints);
        Assert.Equal(TypeRef.Error, invalid.Target);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(type)), mixed.Target);
        Assert.Single(mixed.GenericParameters);

        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(direct)), Assert.Single(context.GlobalVariableSymbols, symbol => symbol.Name.ToString() == "direct").Type);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(specialized)), Assert.Single(context.GlobalVariableSymbols, symbol => symbol.Name.ToString() == "specialized").Type);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(projected)), Assert.Single(context.GlobalVariableSymbols, symbol => symbol.Name.ToString() == "projected").Type);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(mixed)), Assert.Single(context.GlobalVariableSymbols, symbol => symbol.Name.ToString() == "mixed").Type);

        TopLevelAliasDeclaration directSyntax = Assert.IsType<TopLevelAliasDeclaration>(root.Members[1]);
        TopLevelAliasDeclaration specializedSyntax = Assert.IsType<TopLevelAliasDeclaration>(root.Members[2]);
        TopLevelAliasDeclaration projectedSyntax = Assert.IsType<TopLevelAliasDeclaration>(root.Members[3]);
        AssertReference(context, directSyntax.Alias.Target, ResolutionContext.GetHandle(plain));
        GenericType specializedTarget = Assert.IsType<GenericType>(Assert.IsType<QualifiedType>(specializedSyntax.Alias.Target).Right);
        AssertReference(context, specializedTarget, ResolutionContext.GetHandle(generic));
        AssertReference(context, Assert.Single(specializedTarget.GenericArguments), ResolutionContext.GetHandle(Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Int32")));
        GenericType projectedTarget = Assert.IsType<GenericType>(Assert.IsType<QualifiedType>(projectedSyntax.Alias.Target).Right);
        AssertReference(context, Assert.IsType<NamedExpressionGenericArgument>(Assert.Single(projectedTarget.GenericArguments)).Expression, ResolutionContext.GetHandle(projectedParameter));

        TopLevelAliasDeclaration mixedSyntax = Assert.IsType<TopLevelAliasDeclaration>(root.Members[5]);
        GenericType mixedTarget = Assert.IsType<GenericType>(Assert.IsType<QualifiedType>(mixedSyntax.Alias.Target).Right);
        AssertReference(context, mixedTarget, ResolutionContext.GetHandle(type));
        AssertReference(context, Assert.IsType<NamedExpressionGenericArgument>(mixedTarget.GenericArguments[0]).Expression, ResolutionContext.GetHandle(context.GenericParameterSymbols[mixed.GenericParameters[0].ID]));
        AssertReference(context, Assert.IsType<NamedExpressionGenericArgument>(mixedTarget.GenericArguments[1]).Expression, ResolutionContext.GetHandle(Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Int32")));
    }

    [Fact]
    public void Resolve_RecordsExpressionAndLabelReferences()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Value;
            public static Value Identity(Value value) { return value; }
            public static Value Loop(Value parameter)
            {
            again:
                Value local = Identity(parameter);
                goto again;
                return local;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        FunctionSymbol function = Assert.Single(context.FunctionSymbols, symbol => symbol.Name.ToString() == "Loop");
        FunctionSymbol identity = Assert.Single(context.FunctionSymbols, symbol => symbol.Name.ToString() == "Identity");
        ParameterSymbol parameter = Assert.Single(context.ParameterSymbols, symbol => symbol.Name.ToString() == "parameter");
        LocalVariableSymbol local = Assert.Single(context.LocalVariableSymbols);
        LabelSymbol label = Assert.Single(context.LabelSymbols);
        TopLevelFunctionDeclaration declaration = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[2]);
        FunctionBlockBody body = Assert.IsType<FunctionBlockBody>(declaration.Function.Body);
        LocalVariableDeclarationStatement localDeclaration = Assert.IsType<LocalVariableDeclarationStatement>(body.Locals[1]);
        LocalGotoStatement branch = Assert.IsType<LocalGotoStatement>(body.Locals[2]);
        LocalReturnStatement result = Assert.IsType<LocalReturnStatement>(body.Locals[3]);

        CallExpression call = Assert.IsType<CallExpression>(localDeclaration.Declaration.Declarators[0].Initializer?.Initializer);
        AssertReference(context, Assert.IsType<IdentifierNameExpression>(call.Callee), ResolutionContext.GetHandle(identity));
        AssertReference(context, Assert.IsType<IdentifierNameExpression>(Assert.Single(call.Arguments)), ResolutionContext.GetHandle(parameter));
        AssertReference(context, branch, ResolutionContext.GetHandle(label));
        AssertReference(context, Assert.IsType<IdentifierNameExpression>(result.Statement.Expression), ResolutionContext.GetHandle(local));
        Assert.Equal(ResolutionContext.GetHandle(function), label.ContainingFunction);
    }

    [Fact]
    public void Resolve_RecordsCompileTimeAndVariadicGenericParameterMetadata()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            namespace Std { public struct Int32; }
            public struct Example<T, N: int, F: float, C: const, Rest...> where N : Std.Int32;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        TypeSymbol example = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Example");
        Assert.Equal(5, example.GenericParameters.Count);

        GenericParameterSymbol type = context.GenericParameterSymbols[example.GenericParameters[0].ID];
        GenericParameterSymbol integer = context.GenericParameterSymbols[example.GenericParameters[1].ID];
        GenericParameterSymbol floating = context.GenericParameterSymbols[example.GenericParameters[2].ID];
        GenericParameterSymbol constant = context.GenericParameterSymbols[example.GenericParameters[3].ID];
        GenericParameterSymbol variadic = context.GenericParameterSymbols[example.GenericParameters[4].ID];
        TypeSymbol int32 = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Int32");

        Assert.Equal(GenericParameterKind.Type, type.ParameterKind);
        Assert.Equal(GenericParameterKind.Integer, integer.ParameterKind);
        Assert.Equal(GenericParameterKind.Float, floating.ParameterKind);
        Assert.Equal(GenericParameterKind.Constant, constant.ParameterKind);
        Assert.True(variadic.IsVariadic);
        Assert.Equal(ResolutionContext.GetHandle(int32), Assert.Single(integer.Constraints));

        TopLevelTypeDeclaration syntax = Assert.IsType<TopLevelTypeDeclaration>(root.Members[1]);
        TypeConstraintClause constraint = Assert.Single(syntax.Type.Constraints);
        GenericName name = Assert.IsType<GenericName>(syntax.Type.Name);
        AssertReference(context, name.GenericParameters[0], ResolutionContext.GetHandle(type));
        AssertReference(context, name.GenericParameters[1], ResolutionContext.GetHandle(integer));
        AssertReference(context, name.GenericParameters[2], ResolutionContext.GetHandle(floating));
        AssertReference(context, name.GenericParameters[3], ResolutionContext.GetHandle(constant));
        AssertReference(context, name.GenericParameters[4], ResolutionContext.GetHandle(variadic));
        AssertReference(context, constraint.GenericParameter, ResolutionContext.GetHandle(integer));
        AssertReference(context, Assert.IsType<TypeTypeConstraint>(Assert.Single(constraint.Constraints)).Type, ResolutionContext.GetHandle(int32));
    }

    [Fact]
    public void Resolve_ResolvesComplexGenericVariableDeclarationWithLiteralArgument()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Int32;
            public struct MyType<T, N: int>;
            public Int32 count;
            public MyType<Int32, 100> value;
            public MyType<Int32, count> namedValue;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        TypeSymbol int32 = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "Int32");
        TypeSymbol myType = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "MyType");
        GlobalVariableSymbol value = Assert.Single(context.GlobalVariableSymbols, symbol => symbol.Name.ToString() == "value");
        GlobalVariableSymbol count = Assert.Single(context.GlobalVariableSymbols, symbol => symbol.Name.ToString() == "count");
        GlobalVariableSymbol namedValue = Assert.Single(context.GlobalVariableSymbols, symbol => symbol.Name.ToString() == "namedValue");
        TopLevelVariableDeclaration declaration = Assert.IsType<TopLevelVariableDeclaration>(root.Members[3]);
        TopLevelVariableDeclaration namedDeclaration = Assert.IsType<TopLevelVariableDeclaration>(root.Members[4]);
        GenericType type = Assert.IsType<GenericType>(declaration.Declaration.Type);
        GenericType namedType = Assert.IsType<GenericType>(namedDeclaration.Declaration.Type);

        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(myType)), value.Type);
        AssertReference(context, type, ResolutionContext.GetHandle(myType));
        AssertReference(context, Assert.IsType<NamedExpressionGenericArgument>(type.GenericArguments[0]).Expression, ResolutionContext.GetHandle(int32));
        LiteralGenericArgument literal = Assert.IsType<LiteralGenericArgument>(type.GenericArguments[1]);
        Assert.Equal("100", literal.Literal.Value);
        Assert.False(context.ResolvedTree.TryGetReference(literal, out _));
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(myType)), namedValue.Type);
        AssertReference(context, namedType, ResolutionContext.GetHandle(myType));
        AssertReference(context, Assert.IsType<NamedExpressionGenericArgument>(namedType.GenericArguments[0]).Expression, ResolutionContext.GetHandle(int32));
        AssertReference(context, Assert.IsType<NamedExpressionGenericArgument>(namedType.GenericArguments[1]).Expression, ResolutionContext.GetHandle(count));
    }

    [Fact]
    public void Resolve_DiscoversEveryVariableDeclaratorInEachScope()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Value
            {
                public Value firstField, secondField;
                public Value Method()
                {
                    Value firstLocal, secondLocal;
                    return firstLocal;
                }
            }
            public Value firstGlobal, secondGlobal;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        Assert.Equal(["firstGlobal", "secondGlobal"], context.GlobalVariableSymbols.Select(symbol => symbol.Name.ToString()).Order());
        Assert.Equal(["firstField", "secondField"], context.FieldSymbols.Select(symbol => symbol.Name.ToString()).Order());
        Assert.Equal(["firstLocal", "secondLocal"], context.LocalVariableSymbols.Select(symbol => symbol.Name.ToString()).Order());
        Assert.Equal(2, Assert.Single(context.MethodSymbols).LocalVariables.Count);
    }

    [Fact]
    public void Resolve_AssignsInferredTypeRef_ForVarVariables()
    {
        var (_, _, _, root) = CompilerTestBed.Parse("""
            public var global = 10;
            public struct Container
            {
                public void Method()
                {
                    var local = 20;
                }
            }
            """);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        GlobalVariableSymbol global = Assert.Single(context.GlobalVariableSymbols);
        Assert.Equal(TypeRefKind.Inferred, global.Type.Kind);
        Assert.True(global.Type.IsInferred);
        Assert.Null(global.Type.Handle);

        LocalVariableSymbol local = Assert.Single(context.LocalVariableSymbols);
        Assert.Equal(TypeRefKind.Inferred, local.Type.Kind);
        Assert.True(local.Type.IsInferred);
        Assert.Null(local.Type.Handle);
    }

    [Fact]
    public void Resolve_AssignsErrorTypeRef_ForUnresolvedTypes()
    {
        var (_, _, _, root) = CompilerTestBed.Parse("""
            public UnknownType global;
            public struct Container : UnknownBase
            {
                public UnknownType field;
                public UnknownType Property { get; }
                public UnknownType Method(UnknownType param)
                {
                    UnknownType local = param;
                    return local;
                }
            }
            public class Generic<T> where T : UnknownConstraint;
            using BrokenAlias = UnknownTarget;
            """);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        Assert.Equal(TypeRef.Error, Assert.Single(context.GlobalVariableSymbols).Type);
        Assert.Equal(TypeRef.Error, Assert.Single(context.FieldSymbols).Type);
        Assert.Equal(TypeRef.Error, Assert.Single(context.PropertySymbols).Type);
        Assert.Equal(TypeRef.Error, Assert.Single(context.MethodSymbols).ReturnType);
        Assert.Equal(TypeRef.Error, Assert.Single(context.ParameterSymbols).Type);
        Assert.Equal(TypeRef.Error, Assert.Single(context.LocalVariableSymbols).Type);

        TypeSymbol container = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "Container");
        Assert.Equal(TypeRef.Error, Assert.Single(container.BaseTypes));

        GenericParameterSymbol genericParam = Assert.Single(context.GenericParameterSymbols);
        Assert.Equal(TypeRef.Error, Assert.Single(genericParam.Constraints));

        AliasSymbol brokenAlias = Assert.Single(context.AliasSymbols);
        Assert.Equal(TypeRef.Error, brokenAlias.Target);
    }

    [Fact]
    public void Resolve_PreservesUnresolvedTypeRef_BeforeDeclarationPass()
    {
        var (_, _, _, root) = CompilerTestBed.Parse("""
            public struct Value;
            public Value global;
            """);

        var syntaxTree = SyntaxTree.CreateSingleRoot(root);
        var resolvedTree = new ResolvedTree();
        var globalNamespace = new NamespaceTrieNode();
        var symbols = new SymbolStore([], [], [], [], [], [], [], [], [], [], [], [], [], []);
        var scopes = new List<Scope> { new(null) };
        var context = new ResolutionContext(syntaxTree, resolvedTree, globalNamespace, symbols, scopes);

        new SymbolDiscoveryPass().Resolve(context);

        GlobalVariableSymbol global = Assert.Single(context.GlobalVariableSymbols);
        Assert.Equal(TypeRefKind.Unresolved, global.Type.Kind);
        Assert.True(global.Type.IsUnresolved);
        Assert.Null(global.Type.Handle);
    }

    [Fact]
    public void TypeRef_EqualityAndOperators()
    {
        var handle1 = (SymbolKind.Type, new SymbolID(1));
        var handle2 = (SymbolKind.Type, new SymbolID(2));

        TypeRef res1 = TypeRef.Resolved(handle1);
        TypeRef res1Duplicate = TypeRef.Resolved(handle1);
        TypeRef res2 = TypeRef.Resolved(handle2);

        Assert.True(res1.IsResolved);
        Assert.False(res1.IsUnresolved);
        Assert.False(res1.IsInferred);
        Assert.False(res1.IsError);

        Assert.Equal(res1, res1Duplicate);
        Assert.NotEqual(res1, res2);
        Assert.True(res1 == handle1);
        Assert.True(handle1 == res1);
        Assert.False(res1 == handle2);
        Assert.False(res1 != handle1);

        TypeRef inf = TypeRef.Inferred;
        Assert.True(inf.IsInferred);
        Assert.Equal("var", inf.ToString());

        TypeRef err = TypeRef.Error;
        Assert.True(err.IsError);
        Assert.Equal("<error>", err.ToString());

        TypeRef unres = TypeRef.Unresolved;
        Assert.True(unres.IsUnresolved);
        Assert.Equal("<unresolved>", unres.ToString());

        // Implicit conversion from SymbolHandle
        TypeRef fromHandle = handle1;
        Assert.Equal(res1, fromHandle);
    }

    [Fact]
    public void Resolve_ResolvesAliasesAndTypeRefs_InBaseTypes()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Base;
            public struct Secondary;
            public struct GenericBase<T>;
            public struct Int32;

            using BaseAlias = Base;
            using SecondaryAlias = Secondary;
            using ConcreteAlias = GenericBase<Int32>;

            public struct Derived : BaseAlias;
            public struct MultiDerived : BaseAlias, SecondaryAlias;
            public struct GenericDerived : ConcreteAlias;
            using ChainedAlias = BaseAlias;
            public struct ChainedDerived : ChainedAlias;
            public struct Outer
            {
                public struct Nested : BaseAlias;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        AliasSymbol baseAlias = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "BaseAlias");
        AliasSymbol secondaryAlias = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "SecondaryAlias");
        AliasSymbol concreteAlias = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "ConcreteAlias");
        AliasSymbol chainedAlias = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "ChainedAlias");

        var baseAliasHandle = ResolutionContext.GetHandle(baseAlias);
        var secondaryAliasHandle = ResolutionContext.GetHandle(secondaryAlias);
        var concreteAliasHandle = ResolutionContext.GetHandle(concreteAlias);
        var chainedAliasHandle = ResolutionContext.GetHandle(chainedAlias);

        TypeSymbol derived = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "Derived");
        TypeSymbol multiDerived = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "MultiDerived");
        TypeSymbol genericDerived = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "GenericDerived");
        TypeSymbol chainedDerived = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "ChainedDerived");
        NestedTypeSymbol nested = Assert.Single(context.NestedTypeSymbols, s => s.Name.ToString() == "Nested");

        // Verify TypeRef resolution for base types
        Assert.Equal(TypeRef.Resolved(baseAliasHandle), Assert.Single(derived.BaseTypes));
        Assert.True(derived.BaseTypes[0].IsResolved);
        Assert.Equal(TypeRefKind.Resolved, derived.BaseTypes[0].Kind);

        Assert.Equal(2, multiDerived.BaseTypes.Count);
        Assert.Equal(TypeRef.Resolved(baseAliasHandle), multiDerived.BaseTypes[0]);
        Assert.Equal(TypeRef.Resolved(secondaryAliasHandle), multiDerived.BaseTypes[1]);

        Assert.Equal(TypeRef.Resolved(concreteAliasHandle), Assert.Single(genericDerived.BaseTypes));
        Assert.Equal(TypeRef.Resolved(chainedAliasHandle), Assert.Single(chainedDerived.BaseTypes));
        Assert.Equal(TypeRef.Resolved(baseAliasHandle), Assert.Single(nested.BaseTypes));

        // Verify syntax references in ResolvedTree
        TopLevelTypeDeclaration derivedSyntax = Assert.IsType<TopLevelTypeDeclaration>(root.Members[7]);
        AssertReference(context, Assert.Single(derivedSyntax.Type.Base!.BaseTypes), baseAliasHandle);

        TopLevelTypeDeclaration multiDerivedSyntax = Assert.IsType<TopLevelTypeDeclaration>(root.Members[8]);
        AssertReference(context, multiDerivedSyntax.Type.Base!.BaseTypes[0], baseAliasHandle);
        AssertReference(context, multiDerivedSyntax.Type.Base!.BaseTypes[1], secondaryAliasHandle);

        TopLevelTypeDeclaration genericDerivedSyntax = Assert.IsType<TopLevelTypeDeclaration>(root.Members[9]);
        AssertReference(context, Assert.Single(genericDerivedSyntax.Type.Base!.BaseTypes), concreteAliasHandle);

        TopLevelTypeDeclaration chainedDerivedSyntax = Assert.IsType<TopLevelTypeDeclaration>(root.Members[11]);
        AssertReference(context, Assert.Single(chainedDerivedSyntax.Type.Base!.BaseTypes), chainedAliasHandle);

        TopLevelTypeDeclaration outerSyntax = Assert.IsType<TopLevelTypeDeclaration>(root.Members[12]);
        TypeBlockBody outerBody = Assert.IsType<TypeBlockBody>(outerSyntax.Type.Body);
        MemberTypeDeclaration nestedSyntax = Assert.IsType<MemberTypeDeclaration>(outerBody.Members[0]);
        AssertReference(context, Assert.Single(nestedSyntax.Type.Base!.BaseTypes), baseAliasHandle);
    }

    [Fact]
    public void Resolve_ResolvesAliasesAndTypeRefs_InGenericConstraints()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Constraint;
            public struct SecondaryConstraint;
            using ConstraintAlias = Constraint;
            using SecondaryAlias = SecondaryConstraint;

            public struct GenericType<T> where T : ConstraintAlias;
            public struct MultiConstraintType<T> where T : ConstraintAlias, SecondaryAlias;

            public struct Container
            {
                public void Method<T>() where T : ConstraintAlias { }
            }

            public void Function<T>() where T : ConstraintAlias { }

            using Projected<T> where T : ConstraintAlias = GenericType<T>;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        AliasSymbol constraintAlias = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "ConstraintAlias");
        AliasSymbol secondaryAlias = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "SecondaryAlias");
        AliasSymbol projected = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "Projected");

        var constraintAliasHandle = ResolutionContext.GetHandle(constraintAlias);
        var secondaryAliasHandle = ResolutionContext.GetHandle(secondaryAlias);

        // Type generic parameter constraint
        TypeSymbol genericType = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "GenericType");
        GenericParameterSymbol typeParam = context.GenericParameterSymbols[genericType.GenericParameters[0].ID];
        Assert.Equal(TypeRef.Resolved(constraintAliasHandle), Assert.Single(typeParam.Constraints));
        Assert.True(typeParam.Constraints[0].IsResolved);

        // Multi constraint generic parameter
        TypeSymbol multiConstraintType = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "MultiConstraintType");
        GenericParameterSymbol multiParam = context.GenericParameterSymbols[multiConstraintType.GenericParameters[0].ID];
        Assert.Equal(2, multiParam.Constraints.Count);
        Assert.Equal(TypeRef.Resolved(constraintAliasHandle), multiParam.Constraints[0]);
        Assert.Equal(TypeRef.Resolved(secondaryAliasHandle), multiParam.Constraints[1]);

        // Method generic parameter constraint
        MethodSymbol method = Assert.Single(context.MethodSymbols, s => s.Name.ToString() == "Method");
        GenericParameterSymbol methodParam = context.GenericParameterSymbols[method.GenericParameters[0].ID];
        Assert.Equal(TypeRef.Resolved(constraintAliasHandle), Assert.Single(methodParam.Constraints));

        // Function generic parameter constraint
        FunctionSymbol function = Assert.Single(context.FunctionSymbols, s => s.Name.ToString() == "Function");
        GenericParameterSymbol funcParam = context.GenericParameterSymbols[function.GenericParameters[0].ID];
        Assert.Equal(TypeRef.Resolved(constraintAliasHandle), Assert.Single(funcParam.Constraints));

        // Alias generic parameter constraint
        GenericParameterSymbol projectedParam = context.GenericParameterSymbols[projected.GenericParameters[0].ID];
        Assert.Equal(TypeRef.Resolved(constraintAliasHandle), Assert.Single(projectedParam.Constraints));
        Assert.True(projected.HasCompatibleConstraints);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(genericType)), projected.Target);

        // Verify syntax references in ResolvedTree
        TopLevelTypeDeclaration genericTypeSyntax = Assert.IsType<TopLevelTypeDeclaration>(root.Members[4]);
        TypeConstraintClause typeConstraintClause = Assert.Single(genericTypeSyntax.Type.Constraints);
        AssertReference(context, Assert.IsType<TypeTypeConstraint>(Assert.Single(typeConstraintClause.Constraints)).Type, constraintAliasHandle);

        TopLevelTypeDeclaration multiSyntax = Assert.IsType<TopLevelTypeDeclaration>(root.Members[5]);
        TypeConstraintClause multiClause = Assert.Single(multiSyntax.Type.Constraints);
        AssertReference(context, Assert.IsType<TypeTypeConstraint>(multiClause.Constraints[0]).Type, constraintAliasHandle);
        AssertReference(context, Assert.IsType<TypeTypeConstraint>(multiClause.Constraints[1]).Type, secondaryAliasHandle);

        TopLevelFunctionDeclaration funcSyntax = Assert.IsType<TopLevelFunctionDeclaration>(root.Members[7]);
        TypeConstraintClause funcClause = Assert.Single(funcSyntax.Function.Signature.Constraints);
        AssertReference(context, Assert.IsType<TypeTypeConstraint>(Assert.Single(funcClause.Constraints)).Type, constraintAliasHandle);

        TopLevelAliasDeclaration projectedSyntax = Assert.IsType<TopLevelAliasDeclaration>(root.Members[8]);
        TypeConstraintClause projectedClause = Assert.Single(projectedSyntax.Alias.Constraints);
        AssertReference(context, Assert.IsType<TypeTypeConstraint>(Assert.Single(projectedClause.Constraints)).Type, constraintAliasHandle);
    }

    [Fact]
    public void Resolve_ValidatesAliasConstraintCompatibility_WithBaseTypeAliasing()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Base;
            public struct Unrelated;
            using BaseAlias = Base;

            public struct DerivedDirect : Base;
            public struct DerivedViaAlias : BaseAlias;

            public struct Generic<T> where T : Base;

            using ValidDirect = Generic<DerivedDirect>;
            using ValidViaAlias = Generic<DerivedViaAlias>;
            using ValidAliasArg = Generic<BaseAlias>;
            using Invalid = Generic<Unrelated>;

            public struct GrandBase;
            using GrandAlias = GrandBase;
            public struct Middle : GrandAlias;
            using MiddleAlias = Middle;
            public struct Leaf : MiddleAlias;

            public struct GrandChecker<T> where T : GrandBase;
            using ValidLeaf = GrandChecker<Leaf>;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        TypeSymbol generic = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "Generic");
        TypeSymbol grandChecker = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "GrandChecker");

        AliasSymbol validDirect = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "ValidDirect");
        AliasSymbol validViaAlias = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "ValidViaAlias");
        AliasSymbol validAliasArg = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "ValidAliasArg");
        AliasSymbol invalid = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "Invalid");
        AliasSymbol validLeaf = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "ValidLeaf");

        Assert.True(validDirect.HasCompatibleConstraints);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(generic)), validDirect.Target);

        Assert.True(validViaAlias.HasCompatibleConstraints);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(generic)), validViaAlias.Target);

        Assert.True(validAliasArg.HasCompatibleConstraints);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(generic)), validAliasArg.Target);

        Assert.False(invalid.HasCompatibleConstraints);
        Assert.Equal(TypeRef.Error, invalid.Target);

        Assert.True(validLeaf.HasCompatibleConstraints);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(grandChecker)), validLeaf.Target);
    }

    [Fact]
    public void Resolve_ValidatesAliasConstraintCompatibility_WithConstraintAliasing()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Base;
            using BaseAlias = Base;
            public struct DerivedDirect : Base;
            public struct DerivedViaAlias : BaseAlias;

            public struct GenericRequiringAlias<T> where T : BaseAlias;

            using SpecAlias = GenericRequiringAlias<BaseAlias>;
            using SpecViaAlias = GenericRequiringAlias<DerivedViaAlias>;
            using SpecDirect = GenericRequiringAlias<Base>;
            using SpecDerivedDirect = GenericRequiringAlias<DerivedDirect>;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        AliasSymbol specAlias = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "SpecAlias");
        AliasSymbol specViaAlias = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "SpecViaAlias");
        AliasSymbol specDirect = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "SpecDirect");
        AliasSymbol specDerivedDirect = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "SpecDerivedDirect");

        Assert.True(specAlias.HasCompatibleConstraints);
        Assert.True(specViaAlias.HasCompatibleConstraints);
        // Currently, candidate does not satisfy an alias constraint if candidate references the underlying type directly (required is not unwrapped)
        Assert.False(specDirect.HasCompatibleConstraints);
        Assert.False(specDerivedDirect.HasCompatibleConstraints);
    }

    [Fact]
    public void TypeRef_GetType_ResolvesDirectAndAliasedTypes()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct GlobalType;
            public struct Container
            {
                public struct NestedType;
            }

            using DirectGlobalAlias = GlobalType;
            using ChainedGlobalAlias = DirectGlobalAlias;
            using BrokenAlias = UnknownType;

            public GlobalType globalVar;
            public Container.NestedType nestedVar;
            public DirectGlobalAlias aliasVar;
            public ChainedGlobalAlias chainedVar;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        TypeSymbol globalType = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "GlobalType");
        NestedTypeSymbol nestedType = Assert.Single(context.NestedTypeSymbols, s => s.Name.ToString() == "NestedType");

        var globalHandle = ResolutionContext.GetHandle(globalType);
        var nestedHandle = ResolutionContext.GetHandle(nestedType);

        // Direct types via TypeRef
        TypeRef directGlobalRef = TypeRef.Resolved(globalHandle);
        TypeRef directNestedRef = TypeRef.Resolved(nestedHandle);

        Assert.Equal(globalHandle, context.GetType(directGlobalRef));
        Assert.Equal(globalHandle, directGlobalRef.GetType(context));

        Assert.Equal(nestedHandle, context.GetType(directNestedRef));
        Assert.Equal(nestedHandle, directNestedRef.GetType(context));

        GlobalVariableSymbol nestedVar = Assert.Single(context.GlobalVariableSymbols, s => s.Name.ToString() == "nestedVar");
        Assert.Equal(nestedHandle, context.GetType(nestedVar.Type));
        Assert.Equal(nestedHandle, nestedVar.Type.GetType(context));

        // Aliased global types
        AliasSymbol directGlobalAlias = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "DirectGlobalAlias");
        AliasSymbol chainedGlobalAlias = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "ChainedGlobalAlias");
        AliasSymbol brokenAlias = Assert.Single(context.AliasSymbols, s => s.Name.ToString() == "BrokenAlias");

        TypeRef globalAliasRef = TypeRef.Resolved(ResolutionContext.GetHandle(directGlobalAlias));
        TypeRef chainedAliasRef = TypeRef.Resolved(ResolutionContext.GetHandle(chainedGlobalAlias));
        TypeRef brokenAliasRef = TypeRef.Resolved(ResolutionContext.GetHandle(brokenAlias));

        Assert.Equal(globalHandle, context.GetType(globalAliasRef));
        Assert.Equal(globalHandle, globalAliasRef.GetType(context));

        Assert.Equal(globalHandle, context.GetType(chainedAliasRef));
        Assert.Equal(globalHandle, chainedAliasRef.GetType(context));

        Assert.Null(context.GetType(brokenAliasRef));
        Assert.Null(brokenAliasRef.GetType(context));

        // Aliased nested type (via Target set to nestedHandle)
        var nestedAliasHandle = (SymbolKind.Alias, new SymbolID(context.AliasSymbols.Count));
        var nestedAlias = new AliasSymbol(nestedAliasHandle.Item2, context.GlobalScope, new SymbolPart("NestedAlias"), default(NamespaceTrieNode), null)
        {
            Target = TypeRef.Resolved(nestedHandle)
        };
        context.AliasSymbols.Add(nestedAlias);
        TypeRef nestedAliasRef = TypeRef.Resolved(nestedAliasHandle);

        Assert.Equal(nestedHandle, context.GetType(nestedAliasRef));
        Assert.Equal(nestedHandle, nestedAliasRef.GetType(context));

        // Non-resolved TypeRefs
        Assert.Null(context.GetType(TypeRef.Unresolved));
        Assert.Null(TypeRef.Unresolved.GetType(context));

        Assert.Null(context.GetType(TypeRef.Inferred));
        Assert.Null(TypeRef.Inferred.GetType(context));

        Assert.Null(context.GetType(TypeRef.Error));
        Assert.Null(TypeRef.Error.GetType(context));

        // Cyclic aliases
        var cyclicHandleA = (SymbolKind.Alias, new SymbolID(context.AliasSymbols.Count));
        var cyclicHandleB = (SymbolKind.Alias, new SymbolID(context.AliasSymbols.Count + 1));
        var cyclicAliasA = new AliasSymbol(cyclicHandleA.Item2, context.GlobalScope, new SymbolPart("CyclicA"), default(NamespaceTrieNode), null)
        {
            Target = TypeRef.Resolved(cyclicHandleB)
        };
        var cyclicAliasB = new AliasSymbol(cyclicHandleB.Item2, context.GlobalScope, new SymbolPart("CyclicB"), default(NamespaceTrieNode), null)
        {
            Target = TypeRef.Resolved(cyclicHandleA)
        };
        context.AliasSymbols.Add(cyclicAliasA);
        context.AliasSymbols.Add(cyclicAliasB);

        Assert.Null(context.GetType(TypeRef.Resolved(cyclicHandleA)));
        Assert.Null(TypeRef.Resolved(cyclicHandleA).GetType(context));
    }

    private static void AssertReference(ResolutionContext context, SyntaxNode syntax, (SymbolKind Kind, SymbolID ID) expected)
    {
        Assert.True(context.ResolvedTree.TryGetReference(syntax, out var actual));
        Assert.Equal(expected, actual);
    }
}