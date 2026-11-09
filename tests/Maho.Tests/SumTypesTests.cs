using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class SumTypesTests
{
    private const string Prelude = """
        public struct void;
        public struct int32;
        public struct uint8;
        """;

    // ==========================================
    // 1. Enum Declaration & Symbol Discovery
    // ==========================================

    [Fact]
    public void Enum_UnitVariants_DiscoveredCorrectly()
    {
        var compilation = Compilation.FromSource("""
            public enum Color
            {
                Red,
                Green = 2,
                Blue
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        var sumType = Assert.IsType<SumTypeSymbol>(
            Assert.Single(compilation.Context!.TypeSymbols, s => s.Name.Text == "Color"));

        Assert.Equal(3, sumType.EnumVariants.Count);

        var red = compilation.Context.GetEnumVariantSymbol(sumType.EnumVariants[0])!;
        Assert.Equal("Red", red.Name.Text);
        Assert.Equal(EnumVariantKind.Unit, red.VariantKind);
        Assert.Null(red.ExplicitValue);

        var green = compilation.Context.GetEnumVariantSymbol(sumType.EnumVariants[1])!;
        Assert.Equal("Green", green.Name.Text);
        Assert.Equal(EnumVariantKind.Unit, green.VariantKind);
        Assert.NotNull(green.ExplicitValue);

        var blue = compilation.Context.GetEnumVariantSymbol(sumType.EnumVariants[2])!;
        Assert.Equal("Blue", blue.Name.Text);
        Assert.Equal(EnumVariantKind.Unit, blue.VariantKind);
    }

    [Fact]
    public void Enum_TupleVariants_DiscoveredWithParameters()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public enum Event
            {
                KeyPress(int32 code),
                MouseMove(int32 x, int32 y)
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        var sumType = Assert.IsType<SumTypeSymbol>(
            Assert.Single(compilation.Context!.TypeSymbols, s => s.Name.Text == "Event"));

        Assert.Equal(2, sumType.EnumVariants.Count);

        var keyPress = compilation.Context.GetEnumVariantSymbol(sumType.EnumVariants[0])!;
        Assert.Equal("KeyPress", keyPress.Name.Text);
        Assert.Equal(EnumVariantKind.Tuple, keyPress.VariantKind);
        Assert.Single(keyPress.Parameters);

        var mouseMove = compilation.Context.GetEnumVariantSymbol(sumType.EnumVariants[1])!;
        Assert.Equal("MouseMove", mouseMove.Name.Text);
        Assert.Equal(EnumVariantKind.Tuple, mouseMove.VariantKind);
        Assert.Equal(2, mouseMove.Parameters.Count);
    }

    [Fact]
    public void Enum_StructVariants_DiscoveredWithFields()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public enum Message
            {
                Quit,
                Move { int32 x; int32 y; } = 5
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        var sumType = Assert.IsType<SumTypeSymbol>(
            Assert.Single(compilation.Context!.TypeSymbols, s => s.Name.Text == "Message"));

        Assert.Equal(2, sumType.EnumVariants.Count);

        var move = compilation.Context.GetEnumVariantSymbol(sumType.EnumVariants[1])!;
        Assert.Equal("Move", move.Name.Text);
        Assert.Equal(EnumVariantKind.Struct, move.VariantKind);
        Assert.Equal(2, move.Fields.Count);
        Assert.NotNull(move.ExplicitValue);
    }

    [Fact]
    public void Enum_WithDiscriminantType_AndBaseClause()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public interface IDisplay;
            public enum Status with uint8 : IDisplay
            {
                Ok = 0,
                Fail = 1
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        var sumType = Assert.IsType<SumTypeSymbol>(
            Assert.Single(compilation.Context.TypeSymbols, s => s.Name.Text == "Status"));

        Assert.NotNull(sumType.DiscriminantType);
        Assert.Single(sumType.BaseTypes);
    }

    [Fact]
    public void Enum_InPlaceStructVariantCreation_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public enum Shape
            {
                Circle { int32 radius; }
            }
            public class Test
            {
                public void Run()
                {
                    Shape.Circle { radius = 10 };
                }
            }
            """);

        Assert.Empty(compilation.Diagnostics);
    }

    // ==========================================
    // 2. Union Declaration & Algebraic Laws
    // ==========================================

    [Fact]
    public void Union_NominalDeclaration_DiscoveredCorrectly()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct A;
            public struct B;
            public struct C;
            public union U (A | B | C)
            {
                public void Foo() { }
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        var sumType = Assert.IsType<SumTypeSymbol>(
            Assert.Single(compilation.Context.TypeSymbols, s => s.Name.Text == "U"));

        Assert.Equal(3, sumType.UnionConstituents.Count);
        Assert.Single(sumType.Methods);
    }

    [Fact]
    public void Union_AnonymousType_Resolved()
    {
        var compilation = Compilation.FromSource("""
            public struct A;
            public struct B;
            public class Test
            {
                public (A | B) field;
            }
            """);

        Assert.Empty(compilation.Diagnostics);
        var field = Assert.Single(compilation.Context.FieldSymbols);
        Assert.Equal(TypeRefKind.Union, field.Type.Kind);
        var union = Assert.IsType<UnionSemanticType>(field.Type.SpecialType);
        Assert.Equal(2, union.Variants.Count);
    }

    [Fact]
    public void Union_AlgebraicLaws_Associativity_Idempotence_Commutativity()
    {
        var compilation = Compilation.FromSource("""
            public struct A;
            public struct B;
            public struct C;
            """);

        var typeA = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.Text == "A");
        var typeB = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.Text == "B");
        var typeC = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.Text == "C");

        TypeRef refA = TypeRef.Resolved(ResolutionContext.GetHandle(typeA));
        TypeRef refB = TypeRef.Resolved(ResolutionContext.GetHandle(typeB));
        TypeRef refC = TypeRef.Resolved(ResolutionContext.GetHandle(typeC));

        // 1. Commutativity: (A | B) == (B | A)
        TypeRef u1 = TypeRef.Union([refA, refB]);
        TypeRef u2 = TypeRef.Union([refB, refA]);
        Assert.Equal(u1, u2);
        Assert.Equal(u1.GetHashCode(), u2.GetHashCode());

        // 2. Idempotence: (A | A) == (A |), (A | B | A) == (A | B)
        TypeRef uIdem1 = TypeRef.Union([refA, refA]);
        TypeRef uSingle = TypeRef.Union([refA]);
        Assert.Equal(uSingle, uIdem1);
        Assert.NotEqual(refA, uIdem1);
        Assert.Equal("(A |)", uIdem1.ToDisplayString(compilation.Context));

        TypeRef uIdem2 = TypeRef.Union([refA, refB, refA]);
        Assert.Equal(u1, uIdem2);

        // 3. Associativity: (A | (B | C)) == (A | B | C)
        TypeRef uBC = TypeRef.Union([refB, refC]);
        TypeRef uNested = TypeRef.Union([refA, uBC]);
        TypeRef uFlat = TypeRef.Union([refA, refB, refC]);
        Assert.Equal(uFlat, uNested);
    }

    [Fact]
    public void Union_Subtyping_WideningAllowed()
    {
        var compilation = Compilation.FromSource("""
            public struct A;
            public struct B;
            """);

        var typeA = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.Text == "A");
        var typeB = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.Text == "B");

        TypeRef refA = TypeRef.Resolved(ResolutionContext.GetHandle(typeA));
        TypeRef refB = TypeRef.Resolved(ResolutionContext.GetHandle(typeB));
        TypeRef uAB = TypeRef.Union([refA, refB]);

        Assert.True(uAB.IsAssignableFrom(refA, compilation.Context));
        Assert.True(uAB.IsAssignableFrom(refB, compilation.Context));
        Assert.False(refA.IsAssignableFrom(uAB, compilation.Context));
    }

    // ==========================================
    // 3. Pattern Matching
    // ==========================================

    [Fact]
    public void Match_EnumVariants_Exhaustive_CompilesCleanly()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public enum E
            {
                A,
                B(int32 x),
                C { int32 x; }
            }
            public class Test
            {
                public void Check(E e)
                {
                    match (e)
                    {
                        A => 1,
                        B(x) => 2,
                        C { x } => 3
                    };
                }
            }
            """);

        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void Match_EnumVariants_Wildcard_Exhaustive()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public enum E
            {
                A,
                B,
                C
            }
            public class Test
            {
                public void Check(E e)
                {
                    match (e)
                    {
                        A => 1,
                        _ => 0
                    };
                }
            }
            """);

        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void Match_EnumVariants_MissingVariant_EmitsMH0605()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public enum E
            {
                A,
                B,
                C
            }
            public class Test
            {
                public void Check(E e)
                {
                    match (e)
                    {
                        A => 1
                    };
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0605");
        Assert.Contains("missing variant 'B'", diag.Message);
    }

    [Fact]
    public void Match_EnumVariants_InvalidPattern_EmitsMH0607()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public enum E
            {
                A,
                B
            }
            public class Test
            {
                public void Check(E e)
                {
                    match (e)
                    {
                        A => 1,
                        Unknown => 2,
                        _ => 0
                    };
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0607");
        Assert.Contains("pattern 'Unknown' is not a valid variant or constituent of sum type 'E'", diag.Message);
    }

    [Fact]
    public void Match_Union_Exhaustive_CompilesCleanly()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct A;
            public struct B;
            public class Test
            {
                public void Check((A | B) u)
                {
                    match (u)
                    {
                        A a => 1,
                        B b => 2
                    };
                }
            }
            """);

        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void Match_Union_MissingConstituent_EmitsMH0605()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct A;
            public struct B;
            public class Test
            {
                public void Check((A | B) u)
                {
                    match (u)
                    {
                        A a => 1
                    };
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0605");
        Assert.Contains("missing variant 'B'", diag.Message);
    }

    // ==========================================
    // 4. Untagged Sum Types
    // ==========================================

    [Fact]
    public void UntaggedEnum_Definition_DisallowTaggedUsage_EmitsMH0610()
    {
        var compilation = Compilation.FromSource("""
            public untagged enum RawTag
            {
                A,
                B
            }
            public class Test
            {
                public RawTag field;
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0610");
        Assert.Contains("'untagged' type 'RawTag' must be declared with 'untagged' modifier", diag.Message);
    }

    [Fact]
    public void UntaggedEnum_Definition_UntaggedUsage_Succeeds()
    {
        var compilation = Compilation.FromSource("""
            public untagged enum RawTag
            {
                A,
                B
            }
            public class Test
            {
                public untagged RawTag field;
            }
            """);

        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void UntaggedEnum_MatchDisallowed_EmitsMH0606()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public enum E
            {
                A,
                B
            }
            public class Test
            {
                public void Check(untagged E e)
                {
                    match (e)
                    {
                        A => 1,
                        _ => 0
                    };
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0606");
        Assert.Contains("cannot match on untagged sum type", diag.Message);
    }

    [Fact]
    public void UntaggedUnion_MatchDisallowed_EmitsMH0606()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct A;
            public struct B;
            public class Test
            {
                public void Check(untagged (A | B) u)
                {
                    match (u)
                    {
                        _ => 0
                    };
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0606");
        Assert.Contains("cannot match on untagged sum type", diag.Message);
    }

    // ==========================================
    // 5. Method Dispatch on Sum Types
    // ==========================================

    [Fact]
    public void TaggedInstance_CanCall_OrdinaryAndUntaggedMethods()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public enum E
            {
                A,
                B;
                public void NormalMethod() { }
                public untagged void UntaggedMethod() { }
            }
            public class Test
            {
                public void Run(E e)
                {
                    e.NormalMethod();
                    e.UntaggedMethod();
                }
            }
            """);

        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void UntaggedInstance_CallingOrdinaryMethod_EmitsMH0608()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public enum E
            {
                A,
                B;
                public void NormalMethod() { }
                public untagged void UntaggedMethod() { }
            }
            public class Test
            {
                public void Run(untagged E e)
                {
                    e.NormalMethod();
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0608");
        Assert.Contains("cannot invoke ordinary instance method 'NormalMethod' on untagged sum type 'untagged E'", diag.Message);
    }

    [Fact]
    public void UntaggedInstance_CallingUntaggedMethod_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public enum E
            {
                A,
                B;
                public void NormalMethod() { }
                public untagged void UntaggedMethod() { }
            }
            public class Test
            {
                public void Run(untagged E e)
                {
                    e.UntaggedMethod();
                }
            }
            """);

        Assert.Empty(compilation.Diagnostics);
    }

    [Fact]
    public void SumType_DirectVariantMethodInvocation_EmitsMH0609()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct A
            {
                public void MethodOnA() { }
            }
            public struct B;
            public class Test
            {
                public void Run((A | B) u)
                {
                    u.MethodOnA();
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0609");
        Assert.Contains("'MethodOnA' is a variant of sum type '(A | B)'; invoke methods via pattern matching", diag.Message);
    }

    [Fact]
    public void IdempotentUnion_RetainsUnionSemantics_DirectVariantMethodInvocation_EmitsMH0609()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct A
            {
                public void MethodOnA() { }
            }
            public class Test
            {
                public void Run((A | A) u)
                {
                    u.MethodOnA();
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        var diag = Assert.Single(compilation.Diagnostics, d => d.Code == "MH0609");
        Assert.Contains("'MethodOnA' is a variant of sum type '(A |)'; invoke methods via pattern matching", diag.Message);
    }
}
