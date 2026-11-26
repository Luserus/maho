using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Tests;

internal static class CompilerTestBed
{
    public const string FullPrelude = """
        intrinsic attribute IntrinsicType(&Str name);

        intrinsic attribute IntrinsicFunction(&Str signature);

        intrinsic attribute IntrinsicProperty(&Str signature);

        intrinsic attribute OperatorFunction(&Str name);

        [IntrinsicType("Layout")]
        public interface Layout;

        [IntrinsicType("Sized")]
        public interface Sized : Layout;

        [IntrinsicType("Unsized")]
        public interface Unsized : Layout;

        [IntrinsicType("Transfer")]
        public interface Transfer;

        [IntrinsicType("Move")]
        public interface Move<T : this = this> : Transfer
        {
            [OperatorFunction("Move")]
            func Move(mut &this, mut &T src) -> ()
            {
            }
        }

        [IntrinsicType("Copy")]
        public interface Copy<T : this = this> : Transfer
        {
            [OperatorFunction("Copy")]
            func Copy(mut &this, &T src) -> ()
            {
            }
        }

        public interface Destructor : Move
        {
            [OperatorFunction("Destructor")]
            func Drop(mut &this) -> ();
        }

        public interface Put<T : this = this>
        {
            [OperatorFunction("Put")]
            func Put() -> T
            {
                T value;
                return value;
            }
        }

        public interface OwningDeref<TReturn>
        {
            [OperatorFunction("OwningDeref")]
            func OwningDeref(mut &this) -> TReturn;
        }

        public interface AllocDeref<TReturn>
        {
            [OperatorFunction("AllocDeref")]
            func AllocDeref(mut &this) -> TReturn;
        }

        public interface ExplicitCast<TTarget>
        {
            [OperatorFunction("ExplicitCast")]
            func Cast(&this) -> TTarget;
        }

        public interface IndexerGetter<TIndex, TReturn>
        {
            [OperatorFunction("IndexerGetter")]
            func Get(&this, TIndex index) -> TReturn;
        }

        [IntrinsicType("UInt8")]
        public struct uint8;

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

        [IntrinsicType("Metadata")]
        public struct Metadata<T> where T : Layout
        {
            [IntrinsicFunction("Metadata.Size()")]
            public func Size(&this) -> nuint;

            [IntrinsicFunction("Metadata.Align()")]
            public func Align(&this) -> nuint;
        }

        public interface Deref<TReturn>
        {
            [OperatorFunction("Dereference")]
            func Deref(&this) -> TReturn;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Deref<&T>, ExplicitCast<ValType*> where T : Layout
        {
            public nuint raw;
            public Metadata<T> metadata;

            [IntrinsicFunction("Reference.Deref()")]
            func Deref.Deref(&this) -> &T;

            [IntrinsicFunction("Reference.Cast<ValType*>()")]
            func ExplicitCast.Cast(&this) -> ValType*;
        }

        [IntrinsicType("MutableReference")]
        public struct MutableReference<T> : Deref<mut &T>, ExplicitCast<ValType*> where T : Layout
        {
            public nuint raw;
            public Metadata<T> metadata;

            [IntrinsicFunction("MutableReference.Deref()")]
            func Deref.Deref(&this) -> mut &T;

            [IntrinsicFunction("MutableReference.Cast<ValType*>()")]
            func ExplicitCast.Cast(&this) -> ValType*;
        }

        public struct SliceIterator<T, D : int = 1> : Iterator<T>
        {
            public T[]& data;
            public nuint index;

            func Iterator.Next(mut &this) -> T
            {
                return (*this.data)[this.index];
            }
        }

        [IntrinsicType("Slice")]
        public struct Slice<T, D : int = 1> : Iterable<T, SliceIterator<T, D>>
        {
            [IntrinsicProperty("Slice.Length")]
            public nuint Length { get; }

            func Iterable.Iterate(&this) -> SliceIterator<T, D>
            {
                return put SliceIterator<T, D> { data = &this, index = 0 };
            }
        }

        [IntrinsicType("Array")]
        public struct Array<T, N : int, D : int = 1> : Iterable<T, SliceIterator<T, D>>
        {
            [IntrinsicFunction("Array.AsSlice()")]
            public func AsSlice(&this) -> T[]&;

            func Iterable.Iterate(&this) -> SliceIterator<T, D>
            {
                return put SliceIterator<T, D> { data = this.AsSlice(), index = 0 };
            }
        }

        [IntrinsicType("Str")]
        public struct Str : IndexerGetter<nuint, uint8>
        {
            public uint8[] data;

            func IndexerGetter.Get(&this, nuint index) -> uint8
            {
                return this.data[index];
            }
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> : Arrow<&T>, Deref<&T> where T : Layout
        {
            public nuint addr;
            public Metadata<T> metadata;

            [IntrinsicFunction("Pointer.Deref()")]
            func Deref.Deref(&this) -> &T;

            func Arrow.Arrow(&this) -> &T
            {
                return *this;
            }
        }

        [IntrinsicType("MutablePointer")]
        public struct MutablePointer<T> : Arrow<mut &T>, Deref<mut &T> where T : Layout
        {
            public nuint addr;
            public Metadata<T> metadata;

            [IntrinsicFunction("MutablePointer.Deref()")]
            func Deref.Deref(&this) -> mut &T;

            func Arrow.Arrow(&this) -> mut &T
            {
                return *this;
            }
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
            func Deconstruct(&this) -> (T...);
        }

        public interface DeconstructMut<T...>
        {
            [OperatorFunction("DeconstructMut")]
            func Deconstruct(mut &this) -> (mut &T...);
        }

        [IntrinsicType("Tuple")]
        public struct Tuple<T...> : Deconstruct<T...>, DeconstructMut<T...>
        {
            [IntrinsicFunction("Tuple.Deconstruct()")]
            func Deconstruct.Deconstruct(&this) -> (T...);

            [IntrinsicFunction("Tuple.DeconstructMut()")]
            func DeconstructMut.Deconstruct(mut &this) -> (mut &T...);
        }

        public interface Iterator<TItem>
        {
            [OperatorFunction("Iterator")]
            func Next(mut &this) -> TItem;
        }

        public interface Iterable<TItem, TIterator>
        {
            [OperatorFunction("Iterable")]
            func Iterate(&this) -> TIterator;
        }

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            func Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : Callable<TReturn, TParams...>
        {
            public nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        [IntrinsicType("Delegate")]
        public struct Delegate<TReturn, TParams...> : Callable<TReturn, TParams...>
        {
            public nuint codeAddress;
            public ValType& env;
            [IntrinsicFunction("Delegate.Invoke(TParams...)")]
            func Callable.Invoke(&this, TParams args) -> TReturn;
        }

        public interface Allocator
        {
            func Allocate(mut &this, nuint size, nuint alignment) -> ValType*;
            func Deallocate(mut &this, ValType* ptr, nuint size, nuint alignment) -> ();
        }

        public struct GlobalAllocator
        {
            public static mut dyn Allocator mut* instance;

            public func Allocate(nuint size, nuint alignment) -> ValType*
            {
                return (*GlobalAllocator.instance).Allocate(size, alignment);
            }

            public func Free(ValType* ptr, nuint size, nuint alignment) -> ()
            {
                (*GlobalAllocator.instance).Deallocate(ptr, size, alignment);
            }
        }

        public interface Arrow<TReturn>
        {
            [OperatorFunction("IndirectMemberAccess")]
            func Arrow(&this) -> TReturn;
        }

        [IntrinsicType("Box")]
        public struct Box<T> : Destructor, Arrow<&T>, Deref<&T>, OwningDeref<mut &T>, AllocDeref<mut &T> where T : Layout
        {
            public T mut& pointer;

            func Destructor.Drop(mut &this) -> ()
            {
                GlobalAllocator.Free(this.pointer as ValType*, this.pointer.metadata.Size(), this.pointer.metadata.Align());
            }

            func Arrow.Arrow(&this) -> &T
            {
                return *this.pointer;
            }

            func Deref.Deref(&this) -> &T
            {
                return *this.pointer;
            }

            func OwningDeref.OwningDeref(mut &this) -> mut &T
            {
                return *this.pointer;
            }

            func AllocDeref.AllocDeref(mut &this) -> mut &T
            {
                return *this.pointer;
            }
        }

        [IntrinsicType("Throwable")]
        public interface Throwable;

        public interface StaticConstructor
        {
            [OperatorFunction("StaticConstructor")]
            func Init() -> ();
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
