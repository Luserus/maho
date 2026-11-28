using Maho.Resolution;

namespace Maho.Tests;

public sealed class SourceTruthIterationAllocationAndTransferTests
{
    [Fact]
    public void ArrayAndSlice_WithoutIterableConformance_ReportMH0571()
    {
        var compilation = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str& name);
            intrinsic attribute OperatorFunction(Str& name);

            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Int32")] public struct int32;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Array")] public struct Array<T, N : int, D : int = 1>;

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

            public func Test(int32[2] arr, int32[] slice) -> ()
            {
                for (var x in arr) {}
                for (var y in slice) {}
            }
            """);

        Assert.Equal(2, compilation.Diagnostics.Count(d => d.Code == "MH0571"));
    }

    [Fact]
    public void FullPrelude_ArrayAndSliceIteration_EnforcesSharedBorrowDuringLoop_AndAllowsMutationAfter()
    {
        var sliceIterOk = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Run(int32[]& items) -> ()
            {
                int32[3] arr = [1, 2, 3];
                for (var x in arr)
                {
                    int32 copy = x;
                }
                arr[0] = 99;

                for (var y in items)
                {
                    int32 copy2 = y;
                }
            }
            """);

        Assert.False(sliceIterOk.HasErrors, string.Join("\n", sliceIterOk.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var sliceIterType = Assert.Single(sliceIterOk.Context.TypeSymbols, t => t.Name.Text == "SliceIterator");
        Assert.False(sliceIterType.Flags.HasFlag(TypeFlags.Intrinsic));

        var mutateDuringLoop = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func BadLoop() -> ()
            {
                int32[3] arr = [1, 2, 3];
                for (var x in arr)
                {
                    arr[0] = 99;
                }
            }
            """);

        Assert.Contains(mutateDuringLoop.Diagnostics, d => d.Code == "MH1003");
    }

    [Fact]
    public void Str_Indexing_ResolvesThroughIndexerGetter()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func FirstByte(Str& s, nuint idx) -> uint8
            {
                return s[idx];
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Destructor_WithNonMutReceiver_ReportsMH0560()
    {
        var compilation = Compilation.FromSource("""
            public attribute OperatorFunction(&Str name);

            public interface BadDtor
            {
                [OperatorFunction("Destructor")]
                func Drop(&this) -> ();
            }
            """);

        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0560");
    }

    [Fact]
    public void NewAndPut_ValueTypeFallback_CustomAllocator_AndDiagnostics()
    {
        var valid = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Item : ValType
            {
                public int32 v;
            }

            public struct CustomAlloc : Allocator
            {
                public int32 id;
                public func Allocate(this mut&, nuint size, nuint alignment) -> ValType* { return default; }
                public func Deallocate(this mut&, ValType* ptr, nuint size, nuint alignment) -> () {}
            }

            public func CreateItems() -> ()
            {
                Box<Item> b1 = new Box<Item>(new Item { v = 1 });
                Item onStack = new Item { v = 5 };
            }
            """);

        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var implicitAllocFallbackRejected = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Item : ValType
            {
                public int32 v;
            }

            public struct CustomAlloc : Allocator
            {
                public int32 id;
                public func Allocate(this mut&, nuint size, nuint alignment) -> ValType* { return default; }
                public func Deallocate(this mut&, ValType* ptr, nuint size, nuint alignment) -> () {}
            }

            public func BadAllocArgs() -> ()
            {
                CustomAlloc alloc = new CustomAlloc { id = 3 };
                var b2 = new<CustomAlloc> Item { v = 2 };
                var b3 = new(alloc) Item { v = 4 };
            }
            """);

        Assert.Equal(2, implicitAllocFallbackRejected.Diagnostics.Count(d => d.Code == "MH0591"));

        var missingValType = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public class NonVal
            {
                public int32 v;
            }

            public func Bad() -> ()
            {
                var a = new NonVal { v = 1 };
            }
            """);

        Assert.Single(missingValType.Diagnostics, d => d.Code == "MH0589");

        var twoArityBoxRejected = Compilation.FromSource(
            CompilerTestBed.FullPrelude.Replace(
                "public struct Box<T> : Destructor, Arrow<T&>, Deref<T&>, OwningDeref<T mut&> where T : Layout",
                "public struct Box<T, A> : Destructor, Arrow<T&>, Deref<T&>, OwningDeref<T mut&> where T : Layout where A : Allocator") + """
            public struct Item : ValType
            {
                public int32 v;
            }

            public func BadNew() -> ()
            {
                Item x = new Item { v = 1 };
            }
            """);

        Assert.Contains(twoArityBoxRejected.Diagnostics, d => d.Code == "MH0020");
        Assert.DoesNotContain(twoArityBoxRejected.Diagnostics, d => d.Code == "MH0588");
    }

    [Fact]
    public void Mutability_NonCoercion_EnforcedForAliasReferenceAndPointer()
    {
        var invalidAlias = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func BadAlias(int32 x) -> ()
            {
                &int32 imm = x;
                mut &int32 mutAlias = imm;
            }
            """);
        Assert.True(invalidAlias.HasErrors);

        var invalidRef = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func BadRef(int32& imm) -> int32 mut&
            {
                return imm;
            }
            """);
        Assert.True(invalidRef.HasErrors);

        var invalidPtr = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func BadPtr(int32* imm) -> int32 mut*
            {
                return imm;
            }
            """);
        Assert.True(invalidPtr.HasErrors);

        var downgradeValid = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func Downgrade(int32 x, int32 mut& mr, int32 mut* mp) -> ()
            {
                mut &int32 ma = x;
                &int32 a = ma;
                int32& r = mr;
                int32* p = mp;
            }
            """);
        Assert.False(downgradeValid.HasErrors, string.Join("\n", downgradeValid.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Assignment_DispatchesExplicitOperatorWhenImplemented_ElsePrimitiveCopyOrMove()
    {
        var compilation = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface Assign<TRhs>
            {
                [OperatorFunction("Assignment")]
                func Assign(this mut&, TRhs rhs) -> ();
            }

            public struct CustomTarget : Assign<int32>
            {
                public int32 value;
                func Assign.Assign(this mut&, int32 rhs) -> ()
                {
                    this->value = rhs;
                }
            }

            public func Test() -> ()
            {
                CustomTarget t = new CustomTarget { value = 0 };
                t = 42;
                int32 n = 1;
                n = 2;
            }
            """);

        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void TransferHierarchy_DefaultGenericAssumesMove_CopyRejectsMoveAndTransfer_MoveRejectsCopy()
    {
        var genericDefaultMoves = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func TakeTwice<T>(T x) -> ()
            {
                T a = x;
                T b = x;
            }
            """);
        Assert.Contains(genericDefaultMoves.Diagnostics, d => d.Code == "MH1001");

        var copyConstraintRejects = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Handle : Move
            {
                public int32 fd;
            }

            public struct WrapMove
            {
                public Handle inner;
            }

            public func Dup<T>(T x) -> () where T : Copy
            {
                T a = x;
                T b = x;
            }

            public func PassUnconstrained<U>(U u) -> ()
            {
                Dup::<U>(u);
            }

            public func PassMove(Handle h, WrapMove w) -> ()
            {
                Dup::<Handle>(h);
                Dup::<WrapMove>(w);
            }
            """);

        Assert.True(copyConstraintRejects.Diagnostics.Count(d => d.Code == "MH0590") >= 3);

        var structuralCopyAndMoveValid = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct PlainPair
            {
                public int32 a;
                public int32 b;
            }

            public struct Handle : Move
            {
                public int32 fd;
            }

            public struct MoveWrapper
            {
                public Handle inner;
            }

            public func TakeCopy<T>(T x) -> T where T : Copy
            {
                T a = x;
                return x;
            }

            public func TakeMove<T>(T x) -> () where T : Move {}

            public func Run(PlainPair p, Handle h, MoveWrapper w) -> ()
            {
                PlainPair c = TakeCopy::<PlainPair>(p);
                TakeMove::<Handle>(h);
                TakeMove::<MoveWrapper>(w);
            }
            """);

        Assert.False(structuralCopyAndMoveValid.HasErrors, string.Join("\n", structuralCopyAndMoveValid.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var moveRejectsCopy = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public func OnlyMove<T>(T x) -> () where T : Move {}

            public func Run(int32 n) -> ()
            {
                OnlyMove::<int32>(n);
            }
            """);
        Assert.Contains(moveRejectsCopy.Diagnostics, d => d.Code == "MH0590");
    }

    [Fact]
    public void Box_ValueConstruction_SliceAlloc_And_UnsizedCoercion_WorkAndEnforceBoxArity()
    {
        var validClean = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public interface IWidget { func Score(this&) -> int32; }

            public struct Widget : IWidget, Destructor
            {
                public int32 x;
                public int32 y;

                public func Construct(this mut&, int32 x, int32 y) -> ()
                {
                    this->x = x;
                    this->y = y;
                }

                func IWidget.Score(this&) -> int32 { return this->x; }
                func Destructor.Drop(this mut&) -> () {}
            }

            public struct Pool { public int32 id; }
            public interface NewInPool<T : this = this>
            {
                [OperatorFunction("New")]
                func New(Pool p) -> Box<T>;
            }

            public struct PooledItem : NewInPool
            {
                public int32 v;
                func NewInPool.New(Pool p) -> Box<PooledItem>
                {
                    return new Box<PooledItem>(new PooledItem { v = p.id });
                }
            }

            public func BuildBox(Pool pool, nuint count) -> Box<PooledItem>
            {
                Box<Widget> b1 = new Box<Widget>(new Widget(10, 20));
                Box<IWidget> coerced = new Box<Widget>(new Widget(1, 2));
                Box<int32[]> sliceBox = Box::<int32[]>.Alloc(count);
                return new(pool) PooledItem { v = 5 };
            }
            """);

        Assert.False(validClean.HasErrors, string.Join("\n", validClean.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var badTwoArityBox = Compilation.FromSource("""
            intrinsic attribute IntrinsicType(Str& name);
            intrinsic attribute IntrinsicFunction(Str& signature);
            intrinsic attribute OperatorFunction(Str& name);
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("ValueType")] public struct ValType;
            [IntrinsicType("Struct")] public struct Struct : ValType;
            [IntrinsicType("UInt8")] public struct uint8;
            [IntrinsicType("Int32")] public struct int32;
            [IntrinsicType("NUInt")] public struct nuint;
            [IntrinsicType("Str")] public struct Str;
            [IntrinsicType("Metadata")] public struct Metadata<T> where T : Layout;
            [IntrinsicType("MutableReference")] public struct MutableReference<T> where T : Layout;

            [IntrinsicType("Box")]
            public struct Box<T, A> where T : Layout
            {
                public T mut& pointer;
            }
            """);

        Assert.Contains(badTwoArityBox.Diagnostics, d => d.Code == "MH0020");
        Assert.DoesNotContain(badTwoArityBox.Diagnostics, d => d.Code == "MH0588");

        var customGenericAllocNeedsNewOp = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct CustomAlloc : Allocator
            {
                public int32 id;
                public func Allocate(this mut&, nuint size, nuint alignment) -> ValType* { return default; }
                public func Deallocate(this mut&, ValType* ptr, nuint size, nuint alignment) -> () {}
            }
            public struct Item : ValType { public int32 v; }

            public func BadExplicitAlloc() -> ()
            {
                var b = new<CustomAlloc> Item { v = 1 };
            }
            """);

        Assert.Contains(customGenericAllocNeedsNewOp.Diagnostics, d => d.Code == "MH0591");
    }

    [Fact]
    public void StaticStructFields_TypeAccessMutabilityAndSkippedInOwnershipAndNew()
    {
        var valid = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct MoveResource : Destructor
            {
                public int32 handle;
                func Destructor.Drop(this mut&) -> () {}
            }

            public struct Counter
            {
                public static mut int32 mutCount;
                public static int32 immCount;
                public static const int32 constCount = 42;
                public static mut MoveResource staticMove;
                public int32 instanceVal;

                public func ResetStatic() -> ()
                {
                    mutCount = 0;
                    Counter.mutCount = 1;
                }

                public func StaticBump() -> int32
                {
                    mutCount = 2;
                    return Counter.immCount;
                }
            }

            public func RequireCopy<T>(T v) -> () where T : Copy
            {
            }

            public func TestValid(mut Counter c) -> int32
            {
                Counter.mutCount = 10;
                int32 r = Counter.mutCount;
                int32 imm = Counter.immCount;
                int32 cnst = Counter.constCount;
                Counter fresh = new Counter { instanceVal = 7 };
                RequireCopy::<Counter>(fresh);
                return r;
            }
            """);

        Assert.False(valid.HasErrors, string.Join("\n", valid.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var invalid = Compilation.FromSource(CompilerTestBed.FullPrelude + """
            public struct Counter
            {
                public static mut int32 mutCount;
                public static int32 immCount;
                public static const int32 constCount = 42;
                public int32 instanceVal;

                public func BadThisStatic(this&) -> int32
                {
                    return this->mutCount;
                }
            }

            public func TestInvalid(Counter c) -> ()
            {
                Counter.immCount = 1;
                Counter.constCount = 2;
                var a = c.mutCount;
                var b = Counter.instanceVal;
            }
            """);

        var allDiags = string.Join("\n", invalid.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
        Assert.True(invalid.HasErrors, allDiags);
        Assert.True(invalid.Diagnostics.Count(d => d.Code == "MH2020") == 2, allDiags);
        Assert.True(invalid.Diagnostics.Count(d => d.Code == "MH2019") == 1, allDiags);
        Assert.True(invalid.Diagnostics.Count(d => d.Code == "MH2009") == 2, allDiags);
    }
}
