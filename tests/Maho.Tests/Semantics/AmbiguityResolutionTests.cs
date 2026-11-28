namespace Maho.Tests;

public sealed class AmbiguityResolutionTests
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

        public struct Base
        {
            public Int32 id;
        }

        public struct MiddleA : Base
        {
            public Int32 priority;
        }

        public struct MiddleB : Base
        {
            public Int32 priority;
        }

        public struct Derived : MiddleA, MiddleB
        {
            public Int32 d;
        }
        """;

    [Fact]
    public void SiblingMemberCollision_DirectAccess_ReportsMH0556()
    {
        var source = Prelude + """
            public func Test(Derived d) -> Int32
            {
                return d.priority;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0556" && d.Message.Contains("priority"));
    }

    [Fact]
    public void SiblingMemberCollision_DisambiguateViaAs_Succeeds()
    {
        var source = Prelude + """
            public func TestA(Derived d) -> Int32
            {
                return (d as MiddleA).priority;
            }

            public func TestB(Derived d) -> Int32
            {
                return (d as MiddleB).priority;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void SharedRootMember_DiamondInheritance_ResolvesUnambiguously()
    {
        var source = Prelude + """
            public func Test(Derived d) -> Int32
            {
                return d.id;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void DerivedField_ShadowsBaseField_ResolvesDirectly()
    {
        var source = Prelude + """
            public struct ShadowingDerived : MiddleA
            {
                public Int32 priority;
            }

            public func Test(ShadowingDerived d) -> Int32
            {
                return d.priority;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }
}

