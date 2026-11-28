namespace Maho.Tests;

public sealed class DeconstructTests
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
        public struct int32;
        [IntrinsicType("Int64")]
        public struct int64;
        [IntrinsicType("NUInt")]
        public struct nuint;
        [IntrinsicType("Bool")]
        public struct bool;

        [IntrinsicType("Slice")]
        public struct Slice<T, D : int = 1>;

        [IntrinsicType("Metadata")]
        public struct Metadata<T> where T : Layout;

        public interface Arrow<TReturn>
        {
            [OperatorFunction("IndirectMemberAccess")]
            func Arrow(this&) -> TReturn;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Arrow<T&> where T : Layout
        {
            nuint raw;
            Metadata<T> metadata;

            func Arrow.Arrow(this&) -> T&
            {
                return &(*(*this));
            }
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


        public interface Deconstruct<T...>
        {
            [OperatorFunction("Deconstruct")]
            func Deconstruct(this&) -> (T...);
        }

        [IntrinsicType("Tuple")]
        public struct Tuple<T...> : ValType, Deconstruct<T...>
        {
            [IntrinsicFunction("Tuple.Deconstruct()")]
            func Deconstruct.Deconstruct(this&) -> (T...);
        }
        """;

    [Fact]
    public void TupleDeconstruction_Declaration_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> ()
            {
                var (a, b) = (10, 20);
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
        var fn = Assert.Single(compilation.Context!.FunctionSymbols, f => f.Name.Text == "Test");
        Assert.Equal(2, fn.LocalVariables.Count);
        Assert.Contains(fn.LocalVariables, v => v.Name.Text == "a" && v.Type.Handle.HasValue && compilation.Context.GetSymbol(v.Type.Handle.Value)?.Name.Text == "int32");
        Assert.Contains(fn.LocalVariables, v => v.Name.Text == "b" && v.Type.Handle.HasValue && compilation.Context.GetSymbol(v.Type.Handle.Value)?.Name.Text == "int32");
    }

    [Fact]
    public void CustomType_ImplementingDeconstruct_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct Point : Deconstruct<int32, int32>
            {
                public int32 x;
                public int32 y;

                func Deconstruct.Deconstruct(this&) -> (int32, int32)
                {
                    return (this->x, this->y);
                }
            }

            public func Test(Point pt) -> ()
            {
                var (px, py) = pt;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
        var fn = Assert.Single(compilation.Context!.FunctionSymbols, f => f.Name.Text == "Test");
        Assert.Equal(2, fn.LocalVariables.Count);
        Assert.Contains(fn.LocalVariables, v => v.Name.Text == "px" && v.Type.Handle.HasValue && compilation.Context.GetSymbol(v.Type.Handle.Value)?.Name.Text == "int32");
        Assert.Contains(fn.LocalVariables, v => v.Name.Text == "py" && v.Type.Handle.HasValue && compilation.Context.GetSymbol(v.Type.Handle.Value)?.Name.Text == "int32");
    }

    [Fact]
    public void CustomType_AssignmentDeconstruction_Succeeds()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct Point : Deconstruct<int32, int32>
            {
                public int32 x;
                public int32 y;

                func Deconstruct.Deconstruct(this&) -> (int32, int32)
                {
                    return (this->x, this->y);
                }
            }

            public func Test(Point pt) -> ()
            {
                int32 px = 0;
                int32 py = 0;
                (px, py) = pt;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void TupleDeconstruction_ElementCountMismatch_ReportsMH0530()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public func Test() -> ()
            {
                var (a, b, c) = (10, 20);
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2010");
    }

    [Fact]
    public void CustomType_DeconstructionMismatch_ReportsMH0530()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct Point : Deconstruct<int32, int32>
            {
                public int32 x;
                public int32 y;

                func Deconstruct.Deconstruct(this&) -> (int32, int32)
                {
                    return (this->x, this->y);
                }
            }

            public func Test(Point pt) -> ()
            {
                var (a, b, c) = pt;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2010");
    }

    [Fact]
    public void Deconstruction_NonDeconstructableType_ReportsMH0530()
    {
        var compilation = Compilation.FromSource(Prelude + """
            public struct NonDeconstructable
            {
                public int32 x;
            }

            public func Test(NonDeconstructable item) -> ()
            {
                var (a, b) = item;
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2010");
    }
}
