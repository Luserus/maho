using Maho.Resolution;
using Maho.Syntax;
using Xunit;

namespace Maho.Tests;

public sealed class ScopeDirectiveTests
{
    [Fact]
    public void TopLevel_GlobalBlock_ContainsDeclarations()
    {
        var compilation = Compilation.FromSource("""
            global
            {
                public struct TopStruct;
                public TopStruct globalVar;
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context!;

        var typeSym = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "TopStruct");
        var varSym = Assert.Single(context.GlobalVariableSymbols, v => v.Name.ToString() == "globalVar");
        Assert.Equal(TypeRef.Resolved(ResolutionContext.GetHandle(typeSym)), varSym.Type);
    }

    [Fact]
    public void Namespace_GlobalBlock_ContainsDeclarations()
    {
        var compilation = Compilation.FromSource("""
            namespace MyNamespace
            {
                global
                {
                    public struct NestedStruct;
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context!;
        Assert.Contains(context.TypeSymbols, t => t.Name.ToString() == "NestedStruct");
    }

    [Fact]
    public void Type_ConstBlock_ContainsDeclarations()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Container
            {
                const
                {
                    public func Compute() -> void { }
                }
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context!;
        var method = Assert.Single(context.MethodSymbols, m => m.Name.ToString() == "Compute");
        Assert.NotNull(method);
    }

    [Fact]
    public void ScopeDirective_DoesNotPropagateModifiersOrAttributes()
    {
        // Declarations inside a block scope are isolated; modifiers are per-declaration
        var compilation = Compilation.FromSource("""
            global
            {
                public struct S1;
                struct S2;
            }
            """);

        Assert.False(compilation.HasErrors);
        var context = compilation.Context!;

        var s1 = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "S1");
        Assert.True(s1.Flags.HasFlag(TypeFlags.Public));

        var s2 = Assert.Single(context.TypeSymbols, t => t.Name.ToString() == "S2");
        Assert.False(s2.Flags.HasFlag(TypeFlags.Public));
    }
}
