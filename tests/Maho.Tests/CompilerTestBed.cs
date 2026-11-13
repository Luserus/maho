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

        [IntrinsicType("Enum")]
        public struct Enum : ValType;

        [IntrinsicType("Tuple")]
        public struct Tuple<T...> : ValType;

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            public func Invoke(TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            public func Callable.Invoke(TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
        {
            nuint codeAddress;
            Pointer<ValType> env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(TParams args) -> TReturn;
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