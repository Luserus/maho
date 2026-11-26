using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class DynSyntaxTests
{
    [Fact]
    public void DynTypeModifier_PostfixBeforePrefix_DynReference()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Widget
            {
                dyn Target& field;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var topType = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(root.Members));
        var typeDecl = topType.Type;
        var block = Assert.IsType<TypeBlockBody>(typeDecl.Body);
        var field = Assert.IsType<MemberFieldDeclaration>(Assert.Single(block.Members));

        var prefix = Assert.IsType<PrefixModifiedType>(field.Declaration.Type);
        Assert.IsType<DynTypeModifier>(prefix.Modifier);
        var modified = Assert.IsType<ModifiedType>(prefix.Type);
        Assert.IsType<ReferenceTypeModifier>(modified.Modifier);
        var target = Assert.IsType<SimpleType>(modified.Type);
        Assert.Equal("Target", target.Name.Value);
    }

    [Fact]
    public void DynTypeModifier_PostfixBeforePrefix_DynPointer()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Widget
            {
                dyn Target* field;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var topType = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(root.Members));
        var typeDecl = topType.Type;
        var block = Assert.IsType<TypeBlockBody>(typeDecl.Body);
        var field = Assert.IsType<MemberFieldDeclaration>(Assert.Single(block.Members));

        var prefix = Assert.IsType<PrefixModifiedType>(field.Declaration.Type);
        Assert.IsType<DynTypeModifier>(prefix.Modifier);
        var modified = Assert.IsType<ModifiedType>(prefix.Type);
        Assert.IsType<PointerTypeModifier>(modified.Modifier);
        var target = Assert.IsType<SimpleType>(modified.Type);
        Assert.Equal("Target", target.Name.Value);
    }

    [Fact]
    public void DynTypeModifier_PostfixBeforePrefix_SliceAndOptional_WithGrouping()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Widget
            {
                (dyn Item&)[] sliceField;
                dyn Option? optField;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var topType = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(root.Members));
        var typeDecl = topType.Type;
        var block = Assert.IsType<TypeBlockBody>(typeDecl.Body);
        Assert.Equal(2, block.Members.Count);

        var field1 = Assert.IsType<MemberFieldDeclaration>(block.Members[0]);
        var modified1 = Assert.IsType<ModifiedType>(field1.Declaration.Type);
        Assert.IsType<SliceTypeModifier>(modified1.Modifier);
        var grouped1 = Assert.IsType<GroupedType>(modified1.Type);
        var prefix1 = Assert.IsType<PrefixModifiedType>(grouped1.InnerType);
        Assert.IsType<DynTypeModifier>(prefix1.Modifier);

        var field2 = Assert.IsType<MemberFieldDeclaration>(block.Members[1]);
        var prefix2 = Assert.IsType<PrefixModifiedType>(field2.Declaration.Type);
        Assert.IsType<DynTypeModifier>(prefix2.Modifier);
        var modified2 = Assert.IsType<ModifiedType>(prefix2.Type);
        Assert.IsType<OptionalTypeModifier>(modified2.Modifier);
    }

    [Fact]
    public void UntaggedModifier_PostfixBeforePrefix_AndGroupedPointer()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public struct Widget
            {
                Box<(untagged Target)*> field;
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var topType = Assert.IsType<TopLevelTypeDeclaration>(Assert.Single(root.Members));
        var typeDecl = topType.Type;
        var block = Assert.IsType<TypeBlockBody>(typeDecl.Body);
        var field = Assert.IsType<MemberFieldDeclaration>(Assert.Single(block.Members));

        var generic = Assert.IsType<GenericType>(field.Declaration.Type);
        var arg = Assert.Single(generic.GenericArguments);

        var modified = Assert.IsType<ModifiedType>(arg);
        Assert.IsType<PointerTypeModifier>(modified.Modifier);

        var grouped = Assert.IsType<GroupedType>(modified.Type);
        var prefix = Assert.IsType<PrefixModifiedType>(grouped.InnerType);
        Assert.IsType<UntaggedTypeModifier>(prefix.Modifier);
    }

    [Fact]
    public void BaseInitializerClause_ParsesMultipleInitializers()
    {
        var (_, diagnostics, _, root) = CompilerTestBed.Parse("""
            public func Construct(int x) -> () : Base1(x), Base2(42)
            {
            }
            """);

        Assert.Empty(diagnostics.Diagnostics);
        var topFunc = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root.Members));
        var sig = topFunc.Function.Signature;

        Assert.NotNull(sig.BaseClause);
        Assert.Equal(2, sig.BaseClause.Initializers.Count);

        var init1 = sig.BaseClause.Initializers[0];
        var type1 = Assert.IsType<SimpleType>(init1.BaseType);
        Assert.Equal("Base1", type1.Name.Value);
        Assert.Single(init1.Arguments);

        var init2 = sig.BaseClause.Initializers[1];
        var type2 = Assert.IsType<SimpleType>(init2.BaseType);
        Assert.Equal("Base2", type2.Name.Value);
        Assert.Single(init2.Arguments);
    }

    [Fact]
    public void BaseInitializerClause_CombinedWithWhereConstraints_BothOrders()
    {
        var (_, diag1, _, root1) = CompilerTestBed.Parse("""
            public func MethodA<T>() -> () where T : Marker : BaseType(10)
            {
            }
            """);

        Assert.Empty(diag1.Diagnostics);
        var topFunc1 = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root1.Members));
        Assert.Single(topFunc1.Function.Signature.Constraints);
        Assert.NotNull(topFunc1.Function.Signature.BaseClause);

        var (_, diag2, _, root2) = CompilerTestBed.Parse("""
            public func MethodB<T>() -> () : BaseType(20) where T : Marker
            {
            }
            """);

        Assert.Empty(diag2.Diagnostics);
        var topFunc2 = Assert.IsType<TopLevelFunctionDeclaration>(Assert.Single(root2.Members));
        Assert.Single(topFunc2.Function.Signature.Constraints);
        Assert.NotNull(topFunc2.Function.Signature.BaseClause);
    }

    [Fact]
    public void DynTypeRef_PropertiesAndSizedness()
    {
        var ctx = Compilation.FromSource("").Context;
        var targetType = TypeRef.Resolved((SymbolKind.Type, new SymbolID(42)));
        var dynRef = TypeRef.Dyn(targetType);

        Assert.True(dynRef.IsDyn);
        Assert.False(dynRef.IsSized(ctx));
        Assert.NotNull(dynRef.AsDyn);
        Assert.Equal(targetType, dynRef.AsDyn.TargetType);
        Assert.Equal("dyn (Type, 42)", dynRef.ToString());

        var dynPtr = TypeRef.Dyn(TypeRef.Pointer(targetType));
        Assert.True(dynPtr.IsSized(ctx));
        Assert.True(dynPtr.IsDyn);

        var dynRefType = TypeRef.Dyn(TypeRef.Reference(targetType));
        Assert.True(dynRefType.IsSized(ctx));
    }
}
