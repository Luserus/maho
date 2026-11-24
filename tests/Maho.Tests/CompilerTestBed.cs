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

        [IntrinsicType("MutableReference")]
        public struct MutableReference<T> : Deref<T mut&>
        {
            public nuint raw;

            [IntrinsicFunction("MutableReference.Deref()")]
            public func Deref.Deref(&this) -> T mut&;
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

        public interface Deconstruct<T...>
        {
            [OperatorFunction("Deconstruct")]
            public func Deconstruct(&this) -> (T...);
        }

        public interface DeconstructMut<T...>
        {
            [OperatorFunction("DeconstructMut")]
            public func Deconstruct(mut &this) -> (mut &T...);
        }

        [IntrinsicType("Tuple")]
        public struct Tuple<T...> : Deconstruct<T...>, DeconstructMut<T...>
        {
            [IntrinsicFunction("Tuple.Deconstruct()")]
            public func Deconstruct.Deconstruct(&this) -> (T...);

            [IntrinsicFunction("Tuple.DeconstructMut()")]
            public func DeconstructMut.Deconstruct(mut &this) -> (mut &T...);
        }

        public interface Iterator<TItem>
        {
            [OperatorFunction("Iterator")]
            public func Next(mut &this) -> TItem;
        }

        public interface Iterable<TItem, TIterator>
        {
            [OperatorFunction("Iterable")]
            public func Iterate(&this) -> TIterator;
        }

        [IntrinsicType("FixedArray")]
        public struct FixedArray<T, N : int32> : Iterator<T>;

        [IntrinsicType("Array")]
        public struct Array<T> : Iterator<T>
        {
            Pointer<T> data;
            nuint length;
        }

        [IntrinsicType("Span")]
        public struct Span<T>
        {
            public Pointer<T> data;
            public nuint length;
        }

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            public func Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : Callable<TReturn, TParams...>
        {
            public nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : Callable<TReturn, TParams...>
        {
            public nuint codeAddress;
            public Pointer<ValType> env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            public func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        public interface Allocator
        {
            public func Allocate(mut &this, nuint size, nuint alignment) -> ValType*;
            public func Deallocate(mut &this, ValType* ptr, nuint size, nuint alignment) -> ();
        }

        public struct GlobalAllocator : Allocator
        {
            public ValType* handle;

            public func Allocate(mut &this, nuint size, nuint alignment) -> ValType*
            {
                return handle;
            }

            public func Deallocate(mut &this, ValType* ptr, nuint size, nuint alignment) -> ()
            {
            }
        }

        public interface Arrow<TReturn>
        {
            [OperatorFunction("IndirectMemberAccess")]
            public func Arrow(&this) -> TReturn;
        }

        [IntrinsicType("Box")]
        public struct Box<T, A = GlobalAllocator> : Arrow<&T>, Deref<&T> where A : Allocator
        {
            public T* pointer;
            public A allocator;

            public func Arrow.Arrow(&this) -> &T
            {
                return *pointer;
            }

            public func Deref.Deref(&this) -> &T
            {
                return *pointer;
            }
        }

        [IntrinsicType("Throwable")]
        public interface Throwable;

        public interface StaticConstructor
        {
            [OperatorFunction("StaticConstructor")]
            public func Init() -> ();
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
