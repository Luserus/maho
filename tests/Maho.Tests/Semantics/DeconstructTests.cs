namespace Maho.Tests;

public sealed class DeconstructTests
{
    private const string Prelude = """
        intrinsic attribute IntrinsicType(Str name);
        intrinsic attribute IntrinsicFunction(Str signature);
        intrinsic attribute OperatorFunction(Str name);

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

        [IntrinsicType("Reference")]
        public struct Reference<T>
        {
            nuint raw;
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            int8& buffer;
            nuint length;
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T>
        {
            nuint addr;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            public func Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            ValType* env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        public interface Deconstruct<T...>
        {
            [OperatorFunction("Deconstruct")]
            func Deconstruct(&this) -> (T...);
        }

        [IntrinsicType("Tuple")]
        public struct Tuple<T...> : ValType, Deconstruct<T...>
        {
            [IntrinsicFunction("Tuple.Deconstruct()")]
            public func Deconstruct.Deconstruct(&this) -> (T...);
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

                public func Deconstruct.Deconstruct(&this) -> (int32, int32)
                {
                    return (this.x, this.y);
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

                public func Deconstruct.Deconstruct(&this) -> (int32, int32)
                {
                    return (this.x, this.y);
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

                public func Deconstruct.Deconstruct(&this) -> (int32, int32)
                {
                    return (this.x, this.y);
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
