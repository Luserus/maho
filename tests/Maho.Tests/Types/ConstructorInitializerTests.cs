namespace Maho.Tests;

public sealed class ConstructorInitializerTests
{
    private const string Prelude = """
        [IntrinsicType("Layout")]
        public interface Layout;
        [IntrinsicType("Sized")]
        public interface Sized : Layout;
        [IntrinsicType("Unsized")]
        public interface Unsized : Layout;

        intrinsic attribute IntrinsicType(Str& name);
        intrinsic attribute IntrinsicFunction(Str& signature);
        intrinsic attribute OperatorFunction(Str& name);

        [IntrinsicType("UInt8")]
        public struct uint8;
        [IntrinsicType("Int8")]
        public struct int8;
        [IntrinsicType("Int32")]
        public struct Int32;
        [IntrinsicType("NUInt")]
        public struct nuint;

        [IntrinsicType("Slice")]
        public struct Slice<T, D : int = 1>;

        [IntrinsicType("Metadata")]
        public struct Metadata<T> where T : Layout;

        [IntrinsicType("Reference")]
        public struct Reference<T> where T : Layout
        {
            nuint raw;
            Metadata<T> metadata;
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            uint8[] data;
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> where T : Layout
        {
            nuint addr;
            Metadata<T> metadata;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            func Invoke(this&, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func Callable.Invoke(this&, TParams args) -> TReturn;
        }
        """;

    [Fact]
    public void BaseInitializer_OnOrdinaryFunction_EmitsMH0552()
    {
        var source = Prelude + """
            public struct Base;

            public func OrdinaryFunction() -> () : Base()
            {
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0552");
    }

    [Fact]
    public void BaseInitializer_OnOrdinaryMethod_EmitsMH0552()
    {
        var source = Prelude + """
            public struct Base;

            public struct Derived : Base
            {
                public func NormalMethod() -> () : Base()
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0552");
    }

    [Fact]
    public void BaseInitializer_OnConstructor_Succeeds()
    {
        var source = Prelude + """
            public struct Base
            {
                public Int32 x;
                public func Construct(Int32 val)
                {
                    x = val;
                }
            }

            public struct Derived : Base
            {
                public func Construct(Int32 v) : Base(v)
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void BaseInitializer_NonBaseType_EmitsMH0553()
    {
        var source = Prelude + """
            public struct Other;

            public struct Derived
            {
                public func Construct() : Other()
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0553");
    }

    [Fact]
    public void BaseInitializer_DuplicateBase_EmitsMH0554()
    {
        var source = Prelude + """
            public struct Base;

            public struct Derived : Base
            {
                public func Construct() : Base(), Base()
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0554");
    }

    [Fact]
    public void SharedBase_WithoutDefaultConstructor_RequiresExplicitInit_MH0551()
    {
        var source = Prelude + """
            public struct Root
            {
                public Int32 id;
                public func Construct(Int32 i)
                {
                    id = i;
                }
            }

            public struct MiddleA : Root;
            public struct MiddleB : Root;

            public struct Derived : MiddleA, MiddleB
            {
                public func Construct()
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0551" && d.Message.Contains("Root"));
    }

    [Fact]
    public void Diamond_MostDerivedExplicitlyInitializesRoot_Succeeds()
    {
        var source = Prelude + """
            public struct Root
            {
                public Int32 id;
                public func Construct(Int32 i)
                {
                    id = i;
                }
            }

            public struct MiddleA : Root;
            public struct MiddleB : Root;

            public struct Derived : MiddleA, MiddleB
            {
                public func Construct(Int32 i) : Root(i)
                {
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }
}
