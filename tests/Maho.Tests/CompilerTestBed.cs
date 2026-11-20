using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Tests;

internal static class CompilerTestBed
{
    public const string FullPrelude = """
        intrinsic attribute IntrinsicType(Str name);

        intrinsic attribute IntrinsicFunction(Str signature);

        intrinsic attribute OperatorFunction(Str name);

        [IntrinsicType("Int8")]
        public struct int8;

        [IntrinsicType("Int16")]
        public struct int16;

        [IntrinsicType("Int32")]
        public struct int32;

        [IntrinsicType("Int64")]
        public struct int64;

        [IntrinsicType("NUInt")]
        public struct nuint;

        [IntrinsicType("Unit")]
        public struct void;

        [IntrinsicType("Bool")]
        public struct bool;

        public interface Deref<TReturn>
        {
            [OperatorFunction("Dereference")]
            public func Deref(&this) -> TReturn;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Deref<T&>
        {
            public nuint raw;

            [IntrinsicFunction("Reference.Deref()")]
            public func Deref.Deref(&this) -> T&;
        }

        [IntrinsicType("Str")]
        public struct Str
        {
            public int8& buffer;
            public nuint length;
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> : Deref<T&>
        {
            public nuint addr;

            [IntrinsicFunction("Pointer.Deref()")]
            public func Deref.Deref(&this) -> T&;
        }

        [IntrinsicType("ValueType")]
        public struct ValType;

        [IntrinsicType("Struct")]
        public struct Struct : ValType;

        [IntrinsicType("Enum")]
        public struct Enum : ValType;

        [IntrinsicType("Tuple")]
        public struct Tuple<T...> : ValType;

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
        """;

    public static (SourceText Text, DiagnosticsManager Diagnostics, Lexer Lexer) Lex(string source)
    {
        SourceText text = new(source);
        DiagnosticsManager diagnostics = new(text);
        Lexer lexer = new(text, diagnostics);
        lexer.Lex();
        return (text, diagnostics, lexer);
    }

    public static (SourceText Text, DiagnosticsManager Diagnostics, Parser Parser, CompilationUnit Root) Parse(string source)
    {
        var (text, diagnostics, lexer) = Lex(source);
        Parser parser = new(text, diagnostics);
        CompilationUnit root = parser.Parse(lexer.Tokens);
        return (text, diagnostics, parser, root);
    }
}
