using Maho.Resolution;

namespace Maho.Tests;

public sealed class ConstantFoldingTests
{
    private const string ConstantFoldingPrelude = """
        intrinsic attribute IntrinsicType(Str name);
        intrinsic attribute IntrinsicFunction(Str signature);
        intrinsic attribute OperatorFunction(Str name);

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        [IntrinsicType("Enum")]
        public struct Enum : ValType;


        public interface Deref<TReturn>
        {
            [OperatorFunction("Dereference")]
            public func Deref(&this) -> TReturn;
        }

        [IntrinsicType("Int8")]
        public struct int8;

        [IntrinsicType("UInt8")]
        public struct uint8;

        [IntrinsicType("Int16")]
        public struct int16;

        [IntrinsicType("UInt16")]
        public struct uint16;

        [IntrinsicType("Int64")]
        public struct int64;

        [IntrinsicType("UInt64")]
        public struct uint64;

        [IntrinsicType("NUInt")]
        public struct nuint;

        [IntrinsicType("Pointer")]
        public struct Pointer<T>
        {
            public nuint addr;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Deref<T&>
        {
            public nuint raw;
            [IntrinsicFunction("Reference.Deref()")]
            public func Deref.Deref(&this) -> T&;
        }

        public interface LogicalAnd<T> { [OperatorFunction("LogicalAnd")] public func And(bool l, bool r) -> bool; }
        public interface LogicalOr<T> { [OperatorFunction("LogicalOr")] public func Or(bool l, bool r) -> bool; }
        public interface LogicalNot<T> { [OperatorFunction("LogicalNot")] public func Not(bool o) -> bool; }

        [IntrinsicType("Bool")]
        public struct bool : LogicalAnd<bool>, LogicalOr<bool>, LogicalNot<bool>
        {
            [IntrinsicFunction("Bool.LogicalAnd(Bool, Bool)")] public func LogicalAnd.And(bool l, bool r) -> bool;
            [IntrinsicFunction("Bool.LogicalOr(Bool, Bool)")] public func LogicalOr.Or(bool l, bool r) -> bool;
            [IntrinsicFunction("Bool.LogicalNot(Bool)")] public func LogicalNot.Not(bool o) -> bool;
        }

        public interface Add<T> { [OperatorFunction("Addition")] public func Add(T l, T r) -> T; }
        public interface Sub<T> { [OperatorFunction("Subtraction")] public func Sub(T l, T r) -> T; }
        public interface Mul<T> { [OperatorFunction("Multiplication")] public func Mul(T l, T r) -> T; }
        public interface Div<T> { [OperatorFunction("Division")] public func Div(T l, T r) -> T; }
        public interface Rem<T> { [OperatorFunction("Remainder")] public func Rem(T l, T r) -> T; }
        public interface Eq<T> { [OperatorFunction("Equality")] public func Eq(T l, T r) -> bool; }
        public interface Neq<T> { [OperatorFunction("Inequality")] public func Neq(T l, T r) -> bool; }
        public interface Lt<T> { [OperatorFunction("LessThan")] public func Lt(T l, T r) -> bool; }
        public interface Le<T> { [OperatorFunction("LessThanOrEqual")] public func Le(T l, T r) -> bool; }
        public interface Gt<T> { [OperatorFunction("GreaterThan")] public func Gt(T l, T r) -> bool; }
        public interface Ge<T> { [OperatorFunction("GreaterThanOrEqual")] public func Ge(T l, T r) -> bool; }

        [IntrinsicType("Str")]
        public struct Str : Add<Str>, Eq<Str>
        {
            public int8& buffer;
            public nuint length;
            [IntrinsicFunction("Str.Add(Str, Str)")] public func Add.Add(Str l, Str r) -> Str;
            [IntrinsicFunction("Str.Eq(Str, Str)")] public func Eq.Eq(Str l, Str r) -> bool;
        }

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            public func Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            public nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            public nuint codeAddress;
            public Pointer<ValType> env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Int32")]
        public struct int32 : Add<int32>, Sub<int32>, Mul<int32>, Div<int32>, Rem<int32>,
                              Eq<int32>, Neq<int32>, Lt<int32>, Le<int32>, Gt<int32>, Ge<int32>
        {
            [IntrinsicFunction("Int32.Add(Int32, Int32)")] public func Add.Add(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Subtract(Int32, Int32)")] public func Sub.Sub(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Multiply(Int32, Int32)")] public func Mul.Mul(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Divide(Int32, Int32)")] public func Div.Div(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Remainder(Int32, Int32)")] public func Rem.Rem(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Equals(Int32, Int32)")] public func Eq.Eq(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.NotEquals(Int32, Int32)")] public func Neq.Neq(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.LessThan(Int32, Int32)")] public func Lt.Lt(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.LessThanOrEqual(Int32, Int32)")] public func Le.Le(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.GreaterThan(Int32, Int32)")] public func Gt.Gt(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.GreaterThanOrEqual(Int32, Int32)")] public func Ge.Ge(int32 l, int32 r) -> bool;
        }

        [IntrinsicType("Never")]
        public struct Never;
        """;

    [Fact]
    public void IfCondition_EvaluatingToTrue_PrunesFalseBranchAndReturns()
    {
        // 10 - 10 == 0 evaluates to true at compile time.
        // The then branch returns 42. There is no return after the if statement.
        // Because the condition is folded to true, CFG reachability sees the then-branch
        // as unconditionally taken, and does not report MH2017 (MissingReturn).
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public struct Evaluator
            {
                public func Test() -> int32
                {
                    if (10 - 10 == 0)
                    {
                        return 42;
                    }
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void IfCondition_EvaluatingToFalse_PrunesTrueBranch()
    {
        // 10 - 10 != 0 evaluates to false at compile time.
        // The else branch returns 42; the then branch is unreachable.
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public struct Evaluator
            {
                public func Test() -> int32
                {
                    if (10 - 10 != 0)
                    {
                    }
                    else
                    {
                        return 42;
                    }
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void ShortCircuiting_FalseAndDivByZero_FoldsToFalseWithoutCrashing()
    {
        // false && (1 / 0 == 0) should short-circuit to false without division by zero.
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public struct Evaluator
            {
                public func Test() -> int32
                {
                    if (false && (1 / 0 == 0))
                    {
                    }
                    else
                    {
                        return 100;
                    }
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void ShortCircuiting_TrueOrDivByZero_FoldsToTrueWithoutCrashing()
    {
        // true || (1 / 0 == 0) should short-circuit to true.
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public struct Evaluator
            {
                public func Test() -> int32
                {
                    if (true || (1 / 0 == 0))
                    {
                        return 200;
                    }
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void StringConcatenation_FoldedAndCompared_AtCompileTime()
    {
        // "hello" + " " + "world" == "hello world" folds to true at compile time.
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public struct Evaluator
            {
                public func Test() -> int32
                {
                    if ("hello" + " " + "world" == "hello world")
                    {
                        return 1;
                    }
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void ConstVariable_PropagatesToExpression()
    {
        // const int32 x = 5; if (x * 2 == 10) return 1;
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public struct Evaluator
            {
                public func Test() -> int32
                {
                    const int32 x = 5;
                    if (x * 2 == 10)
                    {
                        return 1;
                    }
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void WhileTrue_RecognizedAsDiverging_ReturnsNever()
    {
        // while (1 == 1) { } never terminates; function returning Never succeeds.
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public struct Evaluator
            {
                public func Loop() -> Never
                {
                    while (1 == 1)
                    {
                    }
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void LiteralOverflow_OnAssignment_EmitsMH3001Warning_WithoutFailingTyping()
    {
        // int8 range is [-128, 127]. Assigning 128 should emit MH3001 warning, not error.
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public struct Evaluator
            {
                public func Test() -> ()
                {
                    int8 x = 128;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH3001" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void LiteralOverflow_OnUnsignedAssignment_EmitsMH3001Warning()
    {
        // uint8 range is [0, 255]. Assigning 300 should emit MH3001 warning.
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public struct Evaluator
            {
                public func Test() -> ()
                {
                    uint8 y = 300;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH3001" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void LiteralInRange_DoesNotEmitMH3001()
    {
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public struct Evaluator
            {
                public func Test() -> ()
                {
                    int8 a = 127;
                    uint8 c = 255;
                    uint8 d = 0;
                    int16 e = 32767;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.DoesNotContain(compilation.Diagnostics, d => d.Code == "MH3001");
    }

    [Fact]
    public void Const_DivisionByZero_ReportsMH1500_Error()
    {
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public struct Evaluator
            {
                public const int32 Val = 10 / 0;

                public func Test() -> ()
                {
                    const int32 local = 42 % 0;
                }
            }
            """);

        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH1500" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void NonConst_DivisionByZero_ReportsMH3002_Warning_AndCompiles()
    {
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public struct Evaluator
            {
                public func Test() -> ()
                {
                    int32 x = 10 / 0;
                    int32 y = 20 % 0;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH3002" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Const_Untyped_CollapsesLate_IntoDifferentTypes()
    {
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public const MaxBufferSize = 1024;

            public struct BufferHolder
            {
                public func Test() -> ()
                {
                    int32 a = MaxBufferSize;
                    int64 b = MaxBufferSize;
                    const LocalSize = 256;
                    int32 c = LocalSize;
                    int64 d = LocalSize;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Contains(compilation.Context.ConstantSymbols, c => c.Name.Text == "MaxBufferSize");
    }

    [Fact]
    public void Const_Typed_CollapsesEarly()
    {
        var compilation = Compilation.FromSource($$"""
            {{ConstantFoldingPrelude}}

            public const int32 ExplicitInt = 100;

            public struct Checker
            {
                public const int64 MemberConst = 200;

                public func Test() -> ()
                {
                    int32 a = ExplicitInt;
                    int64 b = MemberConst;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var topConst = compilation.Context.ConstantSymbols.FirstOrDefault(c => c.Name.Text == "ExplicitInt");
        Assert.NotNull(topConst);
        Assert.True(topConst.Type.Handle.HasValue);
        var sym = compilation.Context.GetSymbol(topConst.Type.Handle.Value) as TypeSymbol;
        Assert.NotNull(sym);
        Assert.Equal("int32", sym.Name.Text);
    }
}
