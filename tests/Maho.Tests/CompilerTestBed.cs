using Maho.Diagnostics;
using Maho.Syntax;
using Maho.Text;

namespace Maho.Tests;

internal static class CompilerTestBed
{
    public const string FullPrelude = """
        intrinsic attribute IntrinsicType(Str& name);

        intrinsic attribute IntrinsicFunction(Str& signature);

        intrinsic attribute IntrinsicProperty(Str& signature);

        intrinsic attribute OperatorFunction(Str& name);

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
            func Move(this mut&, T mut& src) -> ()
            {
            }
        }

        [IntrinsicType("Copy")]
        public interface Copy<T : this = this> : Transfer
        {
            [OperatorFunction("Copy")]
            func Copy(this mut&, T& src) -> ()
            {
            }
        }

        public interface Destructor : Move
        {
            [OperatorFunction("Destructor")]
            func Drop(this mut&) -> ();
        }

        public interface OwningDeref<TReturn>
        {
            [OperatorFunction("OwningDeref")]
            func OwningDeref(this mut&) -> TReturn;
        }

        public interface ExplicitCast<TTarget>
        {
            [OperatorFunction("ExplicitCast")]
            func Cast(this&) -> TTarget;
        }

        public interface IndexerGetter<TIndex, TReturn>
        {
            [OperatorFunction("IndexerGetter")]
            func Get(this&, TIndex index) -> TReturn;
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
            public func Size(this&) -> nuint;

            [IntrinsicFunction("Metadata.Align()")]
            public func Align(this&) -> nuint;
        }

        public interface Deref<TReturn>
        {
            [OperatorFunction("Dereference")]
            func Deref(this&) -> TReturn;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Arrow<T&>, Deref<T&>, ExplicitCast<ValType*> where T : Layout
        {
            public nuint raw;
            public Metadata<T> metadata;

            [IntrinsicFunction("Reference.Deref()")]
            func Deref.Deref(this&) -> T&;

            [IntrinsicFunction("Reference.Cast<ValType*>()")]
            func ExplicitCast.Cast(this&) -> ValType*;

            func Arrow.Arrow(this&) -> T&
            {
                return &(*(*this));
            }
        }

        [IntrinsicType("MutableReference")]
        public struct MutableReference<T> : Arrow<T mut&>, Deref<T mut&>, ExplicitCast<ValType*> where T : Layout
        {
            public nuint raw;
            public Metadata<T> metadata;

            [IntrinsicFunction("MutableReference.Deref()")]
            func Deref.Deref(this&) -> T mut&;

            [IntrinsicFunction("MutableReference.Cast<ValType*>()")]
            func ExplicitCast.Cast(this&) -> ValType*;

            func Arrow.Arrow(this&) -> T mut&
            {
                return &(*(*this));
            }
        }

        public struct SliceIterator<T, D : int = 1> : Iterator<T>
        {
            public T[]& data;
            public nuint index;

            func Iterator.Next(this mut&) -> T
            {
                return (*this->data)[this->index];
            }
        }

        [IntrinsicType("Slice")]
        public struct Slice<T, D : int = 1> : Iterable<T, SliceIterator<T, D>>
        {
            [IntrinsicProperty("Slice.Length")]
            public nuint Length { get; }

            func Iterable.Iterate(this&) -> SliceIterator<T, D>
            {
                return new SliceIterator<T, D> { data = this, index = 0 };
            }
        }

        [IntrinsicType("Array")]
        public struct Array<T, N : int, D : int = 1> : Iterable<T, SliceIterator<T, D>>
        {
            [IntrinsicFunction("Array.AsSlice()")]
            public func AsSlice(this&) -> T[]&;

            func Iterable.Iterate(this&) -> SliceIterator<T, D>
            {
                return new SliceIterator<T, D> { data = this->AsSlice(), index = 0 };
            }
        }

        [IntrinsicType("Str")]
        public struct Str : IndexerGetter<nuint, uint8>
        {
            public uint8[] data;

            func IndexerGetter.Get(this&, nuint index) -> uint8
            {
                return this->data[index];
            }
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> : Arrow<T&>, Deref<T&> where T : Layout
        {
            public nuint addr;
            public Metadata<T> metadata;

            [IntrinsicFunction("Pointer.Deref()")]
            func Deref.Deref(this&) -> T&;

            func Arrow.Arrow(this&) -> T&
            {
                return &(*(*this));
            }
        }

        [IntrinsicType("MutablePointer")]
        public struct MutablePointer<T> : Arrow<T mut&>, Deref<T mut&> where T : Layout
        {
            public nuint addr;
            public Metadata<T> metadata;

            [IntrinsicFunction("MutablePointer.Deref()")]
            func Deref.Deref(this&) -> T mut&;

            func Arrow.Arrow(this&) -> T mut&
            {
                return &(*(*this));
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
            func Deconstruct(this&) -> (T...);
        }

        public interface DeconstructMut<T...>
        {
            [OperatorFunction("DeconstructMut")]
            func Deconstruct(this mut&) -> (T mut&...);
        }

        [IntrinsicType("Tuple")]
        public struct Tuple<T...> : Deconstruct<T...>, DeconstructMut<T...>
        {
            [IntrinsicFunction("Tuple.Deconstruct()")]
            func Deconstruct.Deconstruct(this&) -> (T...);

            [IntrinsicFunction("Tuple.DeconstructMut()")]
            func DeconstructMut.Deconstruct(this mut&) -> (T mut&...);
        }

        public interface Iterator<TItem>
        {
            [OperatorFunction("Iterator")]
            func Next(this mut&) -> TItem;
        }

        public interface Iterable<TItem, TIterator>
        {
            [OperatorFunction("Iterable")]
            func Iterate(this&) -> TIterator;
        }

        public interface Callable<TReturn, TParams...>
        {
            [OperatorFunction("Callable")]
            func Invoke(this&, TParams args) -> TReturn;
        }

        public interface CallableMut<TReturn, TParams...>
        {
            [OperatorFunction("CallableMut")]
            func Invoke(this mut&, TParams args) -> TReturn;
        }

        public interface CallableOnce<TReturn, TParams...>
        {
            [OperatorFunction("CallableOnce")]
            func Invoke(this, TParams args) -> TReturn;
        }

        [IntrinsicType("Function")]
        public struct Function<TReturn, TParams...> : Callable<TReturn, TParams...>, CallableMut<TReturn, TParams...>, CallableOnce<TReturn, TParams...>
        {
            public nuint codeAddress;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func Callable.Invoke(this&, TParams args) -> TReturn;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func CallableMut.Invoke(this mut&, TParams args) -> TReturn;
            [IntrinsicFunction("Function.Invoke(TParams...)")]
            func CallableOnce.Invoke(this, TParams args) -> TReturn;
        }

        public interface Allocator
        {
            func Allocate(this mut&, nuint size, nuint alignment) -> ValType*;
            func Deallocate(this mut&, ValType* ptr, nuint size, nuint alignment) -> ();
        }

        public struct GlobalAllocator
        {
            public static mut Allocator mut* instance;

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
            func Arrow(this&) -> TReturn;
        }

        [IntrinsicType("Box")]
        public struct Box<T> : Destructor, Arrow<T&>, Deref<T&>, OwningDeref<T mut&> where T : Layout
        {
            public T mut& pointer;

            [IntrinsicFunction("Box.AllocUninit")]
            public func AllocUninit<TVal>() -> TVal mut*;

            [IntrinsicFunction("Ptr.WriteUninit")]
            public func WriteUninit<TVal>(TVal mut* dst, TVal value) -> ();

            [IntrinsicFunction("Box.FromMutPtr")]
            public func FromMutPtr<TVal>(TVal mut* ptr) -> TVal mut&;

            [IntrinsicFunction("Box.AllocSlice")]
            public func AllocSlice<TVal>(nuint len) -> Box<TVal[]>;

            func Destructor.Drop(this mut&) -> ()
            {
                GlobalAllocator.Free(this->pointer as ValType*, this->pointer.metadata.Size(), this->pointer.metadata.Align());
            }

            func Arrow.Arrow(this&) -> T&
            {
                return this->pointer;
            }

            func Deref.Deref(this&) -> T&
            {
                return this->pointer;
            }

            func OwningDeref.OwningDeref(this mut&) -> T mut&
            {
                return this->pointer;
            }
        }

        public extension<T> Box<T> where T : Sized
        {
            public func Construct(this mut&, T value) -> ()
            {
                T mut* raw = Box::<T>.AllocUninit::<T>();
                Box::<T>.WriteUninit::<T>(raw, value);
                this->pointer = Box::<T>.FromMutPtr::<T>(raw);
            }

            public func Emplace<F>(F init) -> Box<T> where F : CallableOnce<T>
            {
                return new Box<T>(init());
            }
        }

        public extension<T> Box<T[]> where T : Sized
        {
            public func Alloc(nuint len) -> Box<T[]>
            {
                return Box::<T>.AllocSlice::<T>(len);
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
