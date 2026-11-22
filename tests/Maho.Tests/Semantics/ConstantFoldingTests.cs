using System.Linq;
using Maho.Diagnostics;
using Maho.Resolution;
using Maho.Syntax;
using Xunit;

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

        [IntrinsicType("Unit")]
        public struct void;

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

        public interface StrAdd<T> { [OperatorFunction("Addition")] public func Add(Str l, Str r) -> Str; }
        public interface StrEq<T> { [OperatorFunction("Equality")] public func Eq(Str l, Str r) -> bool; }

        [IntrinsicType("Str")]
        public struct Str : StrAdd<Str>, StrEq<Str>
        {
            public int8& buffer;
            public nuint length;
            [IntrinsicFunction("Str.Add(Str, Str)")] public func StrAdd.Add(Str l, Str r) -> Str;
            [IntrinsicFunction("Str.Eq(Str, Str)")] public func StrEq.Eq(Str l, Str r) -> bool;
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

        public interface Add<T> { [OperatorFunction("Addition")] public func Add(int32 l, int32 r) -> int32; }
        public interface Sub<T> { [OperatorFunction("Subtraction")] public func Sub(int32 l, int32 r) -> int32; }
        public interface Mul<T> { [OperatorFunction("Multiplication")] public func Mul(int32 l, int32 r) -> int32; }
        public interface Div<T> { [OperatorFunction("Division")] public func Div(int32 l, int32 r) -> int32; }
        public interface Rem<T> { [OperatorFunction("Remainder")] public func Rem(int32 l, int32 r) -> int32; }
        public interface Eq<T> { [OperatorFunction("Equality")] public func Eq(int32 l, int32 r) -> bool; }
        public interface Neq<T> { [OperatorFunction("Inequality")] public func Neq(int32 l, int32 r) -> bool; }
        public interface Lt<T> { [OperatorFunction("LessThan")] public func Lt(int32 l, int32 r) -> bool; }
        public interface Le<T> { [OperatorFunction("LessThanOrEqual")] public func Le(int32 l, int32 r) -> bool; }
        public interface Gt<T> { [OperatorFunction("GreaterThan")] public func Gt(int32 l, int32 r) -> bool; }
        public interface Ge<T> { [OperatorFunction("GreaterThanOrEqual")] public func Ge(int32 l, int32 r) -> bool; }

        [IntrinsicType("Int32")]
        public struct int32 : Add<int32>, Sub<int32>, Mul<int32>, Div<int32>, Rem<int32>,
                              Eq<int32>, Neq<int32>, Lt<int32>, Le<int32>, Gt<int32>, Ge<int32>
        {
            [IntrinsicFunction("Int32.Add(Int32, Int32)")] public func Add.Add(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Sub(Int32, Int32)")] public func Sub.Sub(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Mul(Int32, Int32)")] public func Mul.Mul(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Div(Int32, Int32)")] public func Div.Div(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Rem(Int32, Int32)")] public func Rem.Rem(int32 l, int32 r) -> int32;
            [IntrinsicFunction("Int32.Eq(Int32, Int32)")] public func Eq.Eq(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.Neq(Int32, Int32)")] public func Neq.Neq(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.Lt(Int32, Int32)")] public func Lt.Lt(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.Le(Int32, Int32)")] public func Le.Le(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.Gt(Int32, Int32)")] public func Gt.Gt(int32 l, int32 r) -> bool;
            [IntrinsicFunction("Int32.Ge(Int32, Int32)")] public func Ge.Ge(int32 l, int32 r) -> bool;
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
                public func Test() -> void
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
                public func Test() -> void
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
                public func Test() -> void
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

                public func Test() -> void
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
                public func Test() -> void
                {
                    int32 x = 10 / 0;
                    int32 y = 20 % 0;
                }
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH3002" && d.Severity == DiagnosticSeverity.Warning);
    }
}
