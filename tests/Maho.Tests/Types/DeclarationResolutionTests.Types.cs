using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed partial class DeclarationResolutionTests
{
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

    [Fact]
    public void Resolve_TupleDestructuringDeclaration_ResolvesIndividualElementTypes()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct TypeA;
            public struct TypeB;
            public struct TypeC;

            public func Test() -> ()
            {
                (TypeA a, TypeB b, TypeC c) = (1, 2, 3);
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        TypeSymbol typeA = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "TypeA");
        TypeSymbol typeB = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "TypeB");
        TypeSymbol typeC = Assert.Single(context.TypeSymbols, symbol => symbol.Name.ToString() == "TypeC");

        var localA = Assert.Single(context.LocalVariableSymbols, symbol => symbol.Name.ToString() == "a");
        var localB = Assert.Single(context.LocalVariableSymbols, symbol => symbol.Name.ToString() == "b");
        var localC = Assert.Single(context.LocalVariableSymbols, symbol => symbol.Name.ToString() == "c");

        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(typeA)), localA.Type);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(typeB)), localB.Type);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(typeC)), localC.Type);
    }

    [Fact]
    public void Resolve_MultipleFunctionsHaveIndependentLocalVariableIdsAndIndexers()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func First() -> ()
            {
                int a = 1;
                int b = 2;
            }

            public func Second() -> ()
            {
                int x = 10;
                int y = 20;
                int z = 30;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        var first = Assert.Single(context.FunctionSymbols, f => f.Name.ToString() == "First");
        var second = Assert.Single(context.FunctionSymbols, f => f.Name.ToString() == "Second");

        Assert.Equal(2, first.LocalVariables.Count);
        Assert.Equal(3, second.LocalVariables.Count);

        // Independent ID spaces starting from 0
        Assert.Equal((SymbolID)0, first[0].ID);
        Assert.Equal((SymbolID)1, first[1].ID);
        Assert.Equal("a", first[0].Name.ToString());
        Assert.Equal("b", first[1].Name.ToString());

        Assert.Equal((SymbolID)0, second[0].ID);
        Assert.Equal((SymbolID)1, second[1].ID);
        Assert.Equal((SymbolID)2, second[2].ID);
        Assert.Equal("x", second[0].Name.ToString());
        Assert.Equal("y", second[1].Name.ToString());
        Assert.Equal("z", second[2].Name.ToString());

        // Indexer with SymbolID and SymbolHandle
        Assert.Same(first.LocalVariables[0], first[first[0].ID]);
        Assert.Same(first.LocalVariables[1], first[ResolutionContext.GetHandle(first[1])]);
        Assert.Same(second.LocalVariables[0], second[second[0].ID]);
        Assert.Same(second.LocalVariables[2], second[ResolutionContext.GetHandle(second[2])]);

        // TryGetLocalVariable
        Assert.True(first.TryGetLocalVariable(0, out var symA));
        Assert.Same(first[0], symA);
        Assert.False(first.TryGetLocalVariable(5, out _));
    }

    [Fact]
    public void Resolve_ParameterAttributes_AreResolvedToSymbolHandles()
    {
        var (sourceText, diagnostics, _, root) = CompilerTestBed.Parse("""
            public attribute Marker;
            public attribute Config(int value);

            public func Foo([Marker] int a, [Config(42)] string b, int c) -> ()
            {
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        var markerAttr = Assert.Single(context.AttributeSymbols, a => a.Name.ToString() == "Marker");
        var configAttr = Assert.Single(context.AttributeSymbols, a => a.Name.ToString() == "Config");

        var paramA = Assert.Single(context.ParameterSymbols, p => p.Name.ToString() == "a");
        var paramB = Assert.Single(context.ParameterSymbols, p => p.Name.ToString() == "b");
        var paramC = Assert.Single(context.ParameterSymbols, p => p.Name.ToString() == "c");

        var handleA = Assert.Single(paramA.Attributes);
        Assert.Equal(ResolutionContext.GetHandle(markerAttr), handleA);

        var handleB = Assert.Single(paramB.Attributes);
        Assert.Equal(ResolutionContext.GetHandle(configAttr), handleB);

        Assert.Empty(paramC.Attributes);
    }

    [Fact]
    public void Resolve_ModifiedTypes_ProduceSpecialSemanticTypes_And_RecordTreeReferences()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Int32;
            public Int32* ptr;
            public Int32& refVal;
            public Int32[]& sliceRefVal;
            public Int32[42] fixedArrVal;
            public Int32? optVal;
            public (Int32&)[] nestedRefSliceVal;

            public func LocalAliases(Int32[]& slRef, Int32 val)
            {
                &Int32[] sliceAliasLocal = *slRef;
                &Int32 aliasLocal = val;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        Assert.DoesNotContain(context.Diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0592");
        var int32 = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "Int32");
        var int32Handle = ResolutionContext.GetHandle(int32);

        var ptr = Assert.Single(context.GlobalVariableSymbols, s => s.Name.ToString() == "ptr");
        var refVal = Assert.Single(context.GlobalVariableSymbols, s => s.Name.ToString() == "refVal");
        var sliceRefVal = Assert.Single(context.GlobalVariableSymbols, s => s.Name.ToString() == "sliceRefVal");
        var fixedArrVal = Assert.Single(context.GlobalVariableSymbols, s => s.Name.ToString() == "fixedArrVal");
        var optVal = Assert.Single(context.GlobalVariableSymbols, s => s.Name.ToString() == "optVal");
        var nestedRefSliceVal = Assert.Single(context.GlobalVariableSymbols, s => s.Name.ToString() == "nestedRefSliceVal");

        var fn = Assert.Single(context.FunctionSymbols, f => f.Name.Text == "LocalAliases");
        var sliceAliasLocal = Assert.Single(fn.LocalVariables, v => v.Name.Text == "sliceAliasLocal");
        var aliasLocal = Assert.Single(fn.LocalVariables, v => v.Name.Text == "aliasLocal");

        Assert.Equal(TypeRefKind.Pointer, ptr.Type.Kind);
        Assert.True(ptr.Type.IsResolved);
        var ptrSem = Assert.IsType<PointerSemanticType>(ptr.Type.SpecialType);
        Assert.Equal(TypeRef.Resolved(int32Handle), ptrSem.ElementType);

        Assert.Equal(TypeRefKind.Reference, refVal.Type.Kind);
        Assert.True(refVal.Type.IsResolved);
        var refSem = Assert.IsType<ReferenceSemanticType>(refVal.Type.SpecialType);
        Assert.Equal(TypeRef.Resolved(int32Handle), refSem.ElementType);

        Assert.Equal(TypeRefKind.Alias, sliceAliasLocal.Type.Kind);
        var aliasSem = Assert.IsType<AliasSemanticType>(sliceAliasLocal.Type.SpecialType);
        Assert.Equal(TypeRefKind.Slice, aliasSem.TargetType.Kind);
        Assert.Equal(TypeRef.Resolved(int32Handle), Assert.IsType<SliceSemanticType>(aliasSem.TargetType.SpecialType).ElementType);

        Assert.Equal(TypeRefKind.Alias, aliasLocal.Type.Kind);
        var localAliasSem = Assert.IsType<AliasSemanticType>(aliasLocal.Type.SpecialType);
        Assert.Equal(TypeRef.Resolved(int32Handle), localAliasSem.TargetType);

        Assert.Equal(TypeRefKind.Reference, sliceRefVal.Type.Kind);
        var sliceRefSem = Assert.IsType<ReferenceSemanticType>(sliceRefVal.Type.SpecialType);
        Assert.Equal(TypeRefKind.Slice, sliceRefSem.ElementType.Kind);

        Assert.Equal(TypeRefKind.Array, fixedArrVal.Type.Kind);
        Assert.True(fixedArrVal.Type.IsResolved);
        var fixedArrSem = Assert.IsType<ArraySemanticType>(fixedArrVal.Type.SpecialType);
        Assert.Equal(TypeRef.Resolved(int32Handle), fixedArrSem.ElementType);

        Assert.Equal(TypeRefKind.Optional, optVal.Type.Kind);
        Assert.True(optVal.Type.IsResolved);
        var optSem = Assert.IsType<OptionalSemanticType>(optVal.Type.SpecialType);
        Assert.Equal(TypeRef.Resolved(int32Handle), optSem.ElementType);

        Assert.Equal(TypeRefKind.Slice, nestedRefSliceVal.Type.Kind);
        Assert.True(nestedRefSliceVal.Type.IsResolved);
        var nestedSem = Assert.IsType<SliceSemanticType>(nestedRefSliceVal.Type.SpecialType);
        Assert.Equal(TypeRefKind.Reference, nestedSem.ElementType.Kind);
        var innerRefSem = Assert.IsType<ReferenceSemanticType>(nestedSem.ElementType.SpecialType);
        Assert.Equal(TypeRef.Resolved(int32Handle), innerRefSem.ElementType);

        // Verify that the inner SimpleType node was recorded in ResolvedTree
        var ptrDecl = Assert.IsType<TopLevelVariableDeclaration>(root.Members[1]);
        var modifiedSyntax = Assert.IsType<ModifiedType>(ptrDecl.Declaration.Type);
        AssertReference(context, modifiedSyntax.Type, int32Handle);
    }

    [Fact]
    public void Resolve_TupleTypes_ProduceTupleSemanticType()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Int32;
            public struct String;
            public (Int32 a, String b) pair;
            public Int32 (x, y, z) uniform;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        var int32 = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "Int32");
        var str = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "String");
        var int32Handle = ResolutionContext.GetHandle(int32);
        var strHandle = ResolutionContext.GetHandle(str);

        var pair = Assert.Single(context.GlobalVariableSymbols, s => s.Name.ToString() == "pair");
        var uniform = Assert.Single(context.GlobalVariableSymbols, s => s.Name.ToString() == "uniform");

        Assert.Equal(TypeRefKind.Tuple, pair.Type.Kind);
        Assert.True(pair.Type.IsResolved);
        var pairSem = Assert.IsType<TupleSemanticType>(pair.Type.SpecialType);
        Assert.Equal(2, pairSem.Elements.Count);
        Assert.Equal(TypeRef.Resolved(int32Handle), pairSem.Elements[0].Type);
        Assert.Equal("a", pairSem.Elements[0].Name);
        Assert.Equal(TypeRef.Resolved(strHandle), pairSem.Elements[1].Type);
        Assert.Equal("b", pairSem.Elements[1].Name);

        Assert.Equal(TypeRefKind.Tuple, uniform.Type.Kind);
        Assert.True(uniform.Type.IsResolved);
        var uniformSem = Assert.IsType<TupleSemanticType>(uniform.Type.SpecialType);
        Assert.Equal(3, uniformSem.Elements.Count);
        Assert.All(uniformSem.Elements, e => Assert.Equal(TypeRef.Resolved(int32Handle), e.Type));
        Assert.Equal("x", uniformSem.Elements[0].Name);
        Assert.Equal("y", uniformSem.Elements[1].Name);
        Assert.Equal("z", uniformSem.Elements[2].Name);
    }

    [Fact]
    public void Resolve_AllowsFunctionOverloading_ByModifierTypes()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Int32;
            public func Process(Int32 value) -> () { }
            public func Process(Int32* value) -> () { }
            public func Process(Int32[]& value) -> () { }
            public func Process((Int32&)[]& value) -> () { }
            public func Process(Int32[10] value) -> () { }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        Assert.Equal(5, context.FunctionSymbols.Count);

        var int32 = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "Int32");
        var int32Handle = ResolutionContext.GetHandle(int32);

        var fnPlain = Assert.Single(context.FunctionSymbols, f => context.ParameterSymbols[f.Parameters[0].ID].Type == TypeRef.Resolved(int32Handle));
        var fnPtr = Assert.Single(context.FunctionSymbols, f => context.ParameterSymbols[f.Parameters[0].ID].Type.Kind == TypeRefKind.Pointer);
        var fnSliceRef = Assert.Single(context.FunctionSymbols, f =>
            context.ParameterSymbols[f.Parameters[0].ID].Type.SpecialType is ReferenceSemanticType rf &&
            rf.ElementType.SpecialType is SliceSemanticType sl &&
            sl.ElementType == TypeRef.Resolved(int32Handle));
        var fnGroupedSliceRef = Assert.Single(context.FunctionSymbols, f =>
            context.ParameterSymbols[f.Parameters[0].ID].Type.SpecialType is ReferenceSemanticType rf &&
            rf.ElementType.SpecialType is SliceSemanticType sl &&
            sl.ElementType.Kind == TypeRefKind.Reference);
        var fnArr = Assert.Single(context.FunctionSymbols, f => context.ParameterSymbols[f.Parameters[0].ID].Type.Kind == TypeRefKind.Array);
    }

    [Fact]
    public void Resolve_Alias_ToSpecialSemanticType()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Int32;
            using IntPtr = Int32*;
            public IntPtr ptr;
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        var int32 = Assert.Single(context.TypeSymbols, s => s.Name.ToString() == "Int32");
        var alias = Assert.Single(context.AliasSymbols, a => a.Name.ToString() == "IntPtr");
        var ptr = Assert.Single(context.GlobalVariableSymbols, s => s.Name.ToString() == "ptr");

        Assert.Equal(TypeRefKind.Pointer, alias.Target.Kind);
        Assert.True(alias.Target.IsResolved);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(alias)), ptr.Type);
        Assert.Equal(TypeRefKind.Pointer, ptr.Type.UnwrapAlias(context).Kind);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(int32)), ((PointerSemanticType)ptr.Type.UnwrapAlias(context).SpecialType!).ElementType);
    }

    [Fact]
    public void Resolve_VarInStatementContext_AlwaysInfers_EvenWithVarTypeInScope()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct var;
            public func Test() -> ()
            {
                var x = 1;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        var method = Assert.Single(context.FunctionSymbols, m => m.Name.ToString() == "Test");
        var local = Assert.Single(method.LocalVariables);
        Assert.Equal("x", local.Name.ToString());
        Assert.True(local.Type.IsInferred);
    }

    [Fact]
    public void Resolve_AccessingVarTypeInStatementContext_RequiresQualifiedName()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Holder
            {
                public struct var;
            }
            public func Test() -> ()
            {
                Holder.var x;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        var method = Assert.Single(context.FunctionSymbols, m => m.Name.ToString() == "Test");
        var local = Assert.Single(method.LocalVariables);
        Assert.Equal("x", local.Name.ToString());
        Assert.False(local.Type.IsInferred);
        Assert.True(local.Type.IsResolved);
        var holder = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "Holder");
        var nestedVar = Assert.Single(context.NestedTypeSymbols, t => t.Name.ToString() == "var");
        Assert.Equal(ResolutionContext.GetHandle(nestedVar), local.Type.Handle);
    }

    [Fact]
    public void Resolve_VarInNonInferenceContext_ResolvesVarTypeOrReportsError()
    {
        // When struct var is in scope, parameter type "var" resolves to struct var
        var (_, diag1, _, root1) = CompilerTestBed.Parse("""
            public struct var;
            public func Test(var p) -> () { }
            """);

        Assert.Empty(diag1.Diagnostics);
        ResolutionContext ctx1 = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root1));
        var fn1 = Assert.Single(ctx1.FunctionSymbols, f => f.Name.ToString() == "Test");
        var varSym = Assert.Single(ctx1.TypeSymbols, t => t.Name.ToString() == "var");
        var param = Assert.Single(ctx1.ParameterSymbols);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(varSym)), param.Type);

        // When struct var is NOT in scope, parameter type "var" reports error
        var (_, diag2, _, root2) = CompilerTestBed.Parse("""
            public func Test(var p) -> () { }
            """);

        Assert.Empty(diag2.Diagnostics);
        ResolutionContext ctx2 = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root2));
        Assert.NotEmpty(ctx2.Diagnostics.Diagnostics);
        Assert.Contains(ctx2.Diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0500");
    }


    [Fact]
    public void Resolve_ThrowawaySymbols_PutInSink_MultipleCanCoexist_NotLoweredOrInGlobalTables()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func _() -> () { }
            public func _(int a) -> () { }
            public struct _
            {
                public int x;
                public func Method() -> () { }
            }
            public struct _
            {
                public int y;
            }
            public func Test() -> ()
            {
                var _ = 1;
                var _ = 2;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        // Primary tables only contain the non-throwaway declarations
        Assert.Single(context.FunctionSymbols, f => f.Name.ToString() == "Test");
        Assert.DoesNotContain(context.FunctionSymbols, f => f.Name.ToString() == "_");
        Assert.DoesNotContain(context.TypeSymbols, t => t.Name.ToString() == "_");
        Assert.Empty(context.FieldSymbols); // x and y are in the sink!

        // Sink symbols contains the throwaway declarations and their member symbols
        Assert.Equal(2, context.SinkSymbols.FunctionSymbols.Count);
        Assert.All(context.SinkSymbols.FunctionSymbols, f => Assert.Equal("_", f.Name.ToString()));

        Assert.Equal(2, context.SinkSymbols.TypeSymbols.Count);
        Assert.All(context.SinkSymbols.TypeSymbols, t => Assert.Equal("_", t.Name.ToString()));

        // Members of struct _ are also in the sink!
        Assert.Equal(2, context.SinkSymbols.FieldSymbols.Count);
        Assert.Single(context.SinkSymbols.MethodSymbols);

        // Local _ variables are in the sink local variables, not Test's primary local variables
        var testMethod = Assert.Single(context.FunctionSymbols, f => f.Name.ToString() == "Test");
        Assert.Empty(testMethod.LocalVariables);
        Assert.Equal(2, context.SinkLocalVariables.Count);
    }

    [Fact]
    public void Resolve_ThrowawaySymbols_ResolutionStillWorksInBody()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Value;
            public func _() -> ()
            {
                Value a;
                Value b = a;
            }
            public struct _
            {
                public Value field;
                public func GetField() -> Value
                {
                    return this.field;
                }
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        var valueSym = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "Value");
        var valueHandle = ResolutionContext.GetHandle(valueSym);

        var sinkFn = Assert.Single(context.SinkSymbols.FunctionSymbols);
        Assert.Equal(2, sinkFn.LocalVariables.Count);
        Assert.Equal(TypeRef.Resolved(valueHandle), sinkFn.LocalVariables[0].Type);
        Assert.Equal(TypeRef.Resolved(valueHandle), sinkFn.LocalVariables[1].Type);

        var sinkType = Assert.Single(context.SinkSymbols.TypeSymbols);
        var sinkField = Assert.Single(context.SinkSymbols.FieldSymbols);
        Assert.Equal(TypeRef.Resolved(valueHandle), sinkField.Type);
        var sinkMethod = Assert.Single(context.SinkSymbols.MethodSymbols);
        Assert.Equal(TypeRef.Resolved(valueHandle), sinkMethod.ReturnType);
    }

    [Fact]
    public void Resolve_ThrowawaySymbols_CannotBeReferenced()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func _() -> () { }
            public struct _ { }
            public func Test() -> ()
            {
                var _ = 1;
                var a = _;
                _ s;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        // Attempting to reference _ as variable or type reports unresolved diagnostics
        Assert.NotEmpty(context.Diagnostics.Diagnostics);
        Assert.Contains(context.Diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0500");
    }

    [Fact]
    public void Resolve_VarInField_NonInferenceContext_ResolvesVarTypeOrReportsError()
    {
        // When struct var is in scope, field type "var" resolves to struct var
        var (_, diag1, _, root1) = CompilerTestBed.Parse("""
            public struct var;
            public struct Model
            {
                public var field;
            }
            """);

        Assert.Empty(diag1.Diagnostics);
        ResolutionContext ctx1 = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root1));
        var varSym = Assert.Single(ctx1.TypeSymbols, t => t.Name.ToString() == "var");
        var field = Assert.Single(ctx1.FieldSymbols);
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(varSym)), field.Type);

        // When struct var is NOT in scope, field type "var" reports unresolved type error
        var (_, diag2, _, root2) = CompilerTestBed.Parse("""
            public struct Model
            {
                public var field;
            }
            """);

        Assert.Empty(diag2.Diagnostics);
        ResolutionContext ctx2 = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root2));
        Assert.NotEmpty(ctx2.Diagnostics.Diagnostics);
        Assert.Contains(ctx2.Diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0500");
    }

    [Fact]
    public void Resolve_ThrowawayParameters_CanCoexist_AndRoutedToSink()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Handler(int _, int _) -> () { }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        // No parameters in primary ParameterSymbols, both are in SinkSymbols.ParameterSymbols
        Assert.Empty(context.ParameterSymbols);
        Assert.Equal(2, context.SinkSymbols.ParameterSymbols.Count);
        Assert.All(context.SinkSymbols.ParameterSymbols, p => Assert.Equal("_", p.Name.ToString()));
    }

    [Fact]
    public void Resolve_ThrowawayDeclaration_TemporarilyInactivatesDeclaration()
    {
        // Renaming an active function to _ inactivates it:
        // it cannot be called from active code, but its body resolution still validates types
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct User;
            public func ActiveFn(User u) -> () { }
            // Temporarily inactivated function for testing
            public func _(User u) -> ()
            {
                ActiveFn(u);
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        // Only ActiveFn is in global FunctionSymbols
        Assert.Single(context.FunctionSymbols, f => f.Name.ToString() == "ActiveFn");
        Assert.DoesNotContain(context.FunctionSymbols, f => f.Name.ToString() == "_");

        // The inactivated function is in SinkSymbols
        var sinkFn = Assert.Single(context.SinkSymbols.FunctionSymbols);
        Assert.Equal("_", sinkFn.Name.ToString());
    }

    [Fact]
    public void Resolve_AttributeDeclaration_ResolvesOwnAttributesAndSignatureParameters()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Int32;
            public struct String;
            public attribute Tag;

            [Tag]
            public attribute Config([Tag] Int32 count, String name);

            public struct Container
            {
                public attribute MemberConfig(Int32 id);
            }

            public attribute _(Int32 sinkParam);
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));

        var tagAttr = Assert.Single(context.AttributeSymbols, a => a.Name.ToString() == "Tag");
        var configAttr = Assert.Single(context.AttributeSymbols, a => a.Name.ToString() == "Config");
        var int32Sym = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "Int32");
        var stringSym = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "String");

        // Attribute resolves its own attributes
        var configAttrHandle = Assert.Single(configAttr.Attributes);
        Assert.Equal(ResolutionContext.GetHandle(tagAttr), configAttrHandle);

        // Attribute resolves its signature parameters
        Assert.Equal(2, configAttr.Parameters.Count);
        var countParam = context.GetParameterSymbol(configAttr.Parameters[0]);
        var nameParam = context.GetParameterSymbol(configAttr.Parameters[1]);
        Assert.NotNull(countParam);
        Assert.NotNull(nameParam);

        Assert.Equal("count", countParam.Name.ToString());
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(int32Sym)), countParam.Type);
        var countAttrHandle = Assert.Single(countParam.Attributes);
        Assert.Equal(ResolutionContext.GetHandle(tagAttr), countAttrHandle);

        Assert.Equal("name", nameParam.Name.ToString());
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(stringSym)), nameParam.Type);

        // Nested attribute resolves its parameters
        var memberAttr = Assert.Single(context.NestedAttributeSymbols, a => a.Name.ToString() == "MemberConfig");
        Assert.Single(memberAttr.Parameters);
        var idParam = context.GetParameterSymbol(memberAttr.Parameters[0]);
        Assert.NotNull(idParam);
        Assert.Equal("id", idParam.Name.ToString());
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(int32Sym)), idParam.Type);

        // Sink attribute resolves its parameters in sink
        var sinkAttr = Assert.Single(context.SinkSymbols.AttributeSymbols, a => a.Name.ToString() == "_");
        Assert.Single(sinkAttr.Parameters);
        var sinkParam = context.GetParameterSymbol(sinkAttr.Parameters[0]);
        Assert.NotNull(sinkParam);
        Assert.Equal("sinkParam", sinkParam.Name.ToString());
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(int32Sym)), sinkParam.Type);
        Assert.Contains(sinkParam, context.SinkSymbols.ParameterSymbols);
    }

    [Fact]
    public void Resolve_AttributeDeclaration_UnresolvedParameterType_ReportsDiagnostic()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public attribute Bad(UnknownType x);
            """);

        Assert.Empty(diagnostics.Diagnostics);

        ResolutionContext context = new Resolver().Resolve(SyntaxTree.CreateSingleRoot(root));
        Assert.NotEmpty(context.Diagnostics.Diagnostics);
        Assert.Contains(context.Diagnostics.Diagnostics, d => d.DiagnosticCode == "MH0500");
    }



    private static void AssertReference(ResolutionContext context, SyntaxNode syntax, SymbolHandle expected)
    {
        Assert.True(context.ResolvedTree.TryGetReference(syntax, out var actual));
        Assert.Equal(expected, actual);
    }
}
