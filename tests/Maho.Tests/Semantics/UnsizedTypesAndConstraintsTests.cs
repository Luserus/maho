using System.Linq;
using Maho.Resolution;
using Maho.Syntax;
using Xunit;

namespace Maho.Tests.Semantics;

public sealed class UnsizedTypesAndConstraintsTests
{
    private const string Prelude = CompilerTestBed.FullPrelude;

    [Fact]
    public void Intrinsics_LayoutSizedUnsized_SliceArrayStr_And_LiteralsCollapseToRefStr()
    {
        var source = Prelude + """
            public attribute CustomTag(&Str tag);

            public struct Packet
            {
                public int32 header;
                public uint8[] payload;
            }

            [CustomTag("v1")]
            public func Run() -> &Str
            {
                var literal = "hello";
                var named = nameof(Packet);
                return literal;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var ctx = compilation.Context!;
        Assert.True(ctx.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Layout, out _));
        Assert.True(ctx.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Sized, out _));
        Assert.True(ctx.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Unsized, out _));
        Assert.True(ctx.DiscoveredIntrinsics.TryGet(KnownIntrinsics.Str, out var strH));
        Assert.False(TypeRef.Resolved(strH).IsSized(ctx));
        Assert.True(TypeRef.Alias(TypeRef.Resolved(strH)).IsSized(ctx));

        var runFn = Assert.Single(ctx.FunctionSymbols, f => f.Name.Text == "Run");
        var litVar = Assert.Single(runFn.LocalVariables, v => v.Name.Text == "literal");
        var namedVar = Assert.Single(runFn.LocalVariables, v => v.Name.Text == "named");

        var litAlias = Assert.IsType<AliasSemanticType>(litVar.Type.SpecialType);
        Assert.Equal(strH, litAlias.TargetType.Handle);
        var namedAlias = Assert.IsType<AliasSemanticType>(namedVar.Type.SpecialType);
        Assert.Equal(strH, namedAlias.TargetType.Handle);
    }

    [Fact]
    public void Attribute_WithBareUnsizedStrParameter_ReportsMH0541AndMH0580()
    {
        var intrinsicBadSource = """
            [IntrinsicType("ValueType")] public struct ValType;
            [IntrinsicType("Struct")] public struct Struct : ValType;
            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;
            [IntrinsicType("UInt8")] public struct uint8;
            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Str")] public struct Str { public uint8[] data; }
            intrinsic attribute IntrinsicType(&Str name);
            intrinsic attribute OperatorFunction(Str name);
            """;

        var intrinsicComp = Compilation.FromSource(intrinsicBadSource);
        Assert.True(intrinsicComp.HasErrors);
        Assert.Contains(intrinsicComp.Diagnostics, d => d.Code == "MH0541");
        Assert.Contains(intrinsicComp.Diagnostics, d => d.Code == "MH0580");

        var userBadComp = Compilation.FromSource(Prelude + """
            public attribute BadTag(Str name);
            """);
        Assert.True(userBadComp.HasErrors);
        Assert.Contains(userBadComp.Diagnostics, d => d.Code == "MH0580");
    }

    [Fact]
    public void PostfixBeforePrefix_And_GroupedType_Precedence_AndStatementDisambiguation()
    {
        var source = Prelude + """
            public interface MulOp { [OperatorFunction("Multiplication")] func Mul(Num l, Num r) -> Num; }
            public interface BitAndOp { [OperatorFunction("BitwiseAnd")] func BitAnd(Num l, Num r) -> Num; }

            public struct Num : MulOp, BitAndOp
            {
                public int32 val;
                func MulOp.Mul(Num l, Num r) -> Num { return l; }
                func BitAndOp.BitAnd(Num l, Num r) -> Num { return l; }
            }

            public interface IGreetable
            {
                func Greet(&this) -> int32;
            }

            public union Choice(int32 | bool);

            public struct PtrFields
            {
                public (int32)* ptrToInt;
                public (untagged Choice)* untaggedChoicePtr;
                public (untagged (int32 | bool))* untaggedAnonUnionPtr;
            }

            public struct SliceOfInt { public (int32)[] data; }
            public struct SliceOfAlias { public (&int32)[] data; }
            public struct SliceOfDynAlias { public (dyn &IGreetable)[] data; }
            public struct SliceOfDynRef { public (dyn IGreetable&)[] data; }
            public struct SliceOfStrAlias { public (&Str)[] data; }

            public func AcceptAlias(&int32[] a, int32[]& r, &((&Str)[]) args, dyn &((dyn &IGreetable)[]) d, &((&int32)[]) grouped) -> ()
            {
            }

            public func StatementsDisambiguation() -> Num
            {
                Num a = default;
                Num b = default;
                (a) * b;
                (a) & b;
                (int32)* ptr = default;
                ((&int32)[])* slicePtr = default;
                return (a) * b;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var ctx = compilation.Context!;
        var ptrSym = Assert.IsAssignableFrom<ProductTypeSymbol>(Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "PtrFields"));
        var ptrFields = ptrSym.Fields.Select(h => ctx.GetFieldSymbol(h)!).ToDictionary(f => f.Name.Text, f => f.Type);

        Assert.IsType<PointerSemanticType>(ptrFields["ptrToInt"].SpecialType);
        var uPtr = Assert.IsType<PointerSemanticType>(ptrFields["untaggedChoicePtr"].SpecialType);
        var untaggedChoice = Assert.IsType<UntaggedSemanticType>(uPtr.ElementType.SpecialType);
        var choiceSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "Choice");
        Assert.Equal(ResolutionContext.GetHandle(choiceSym), untaggedChoice.InnerType.Handle);

        var anonPtr = Assert.IsType<PointerSemanticType>(ptrFields["untaggedAnonUnionPtr"].SpecialType);
        var unionSem = Assert.IsType<UnionSemanticType>(anonPtr.ElementType.SpecialType);
        Assert.True(unionSem.IsUntagged);

        var sliceAliasSym = Assert.IsAssignableFrom<ProductTypeSymbol>(Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "SliceOfAlias"));
        var sliceAliasType = ctx.GetFieldSymbol(sliceAliasSym.Fields[0])!.Type;
        var sliceOfAlias = Assert.IsType<SliceSemanticType>(sliceAliasType.SpecialType);
        Assert.IsType<AliasSemanticType>(sliceOfAlias.ElementType.SpecialType);
        Assert.False(sliceAliasType.IsSized(ctx));

        var sliceDynAliasSym = Assert.IsAssignableFrom<ProductTypeSymbol>(Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "SliceOfDynAlias"));
        var sliceOfDynAlias = Assert.IsType<SliceSemanticType>(ctx.GetFieldSymbol(sliceDynAliasSym.Fields[0])!.Type.SpecialType);
        var elemDynAlias = Assert.IsType<DynSemanticType>(sliceOfDynAlias.ElementType.SpecialType);
        Assert.IsType<AliasSemanticType>(elemDynAlias.TargetType.SpecialType);

        var sliceDynRefSym = Assert.IsAssignableFrom<ProductTypeSymbol>(Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "SliceOfDynRef"));
        var sliceOfDynRef = Assert.IsType<SliceSemanticType>(ctx.GetFieldSymbol(sliceDynRefSym.Fields[0])!.Type.SpecialType);
        var elemDynRef = Assert.IsType<DynSemanticType>(sliceOfDynRef.ElementType.SpecialType);
        Assert.IsType<ReferenceSemanticType>(elemDynRef.TargetType.SpecialType);

        var acceptFn = Assert.Single(ctx.FunctionSymbols, f => f.Name.Text == "AcceptAlias");
        var pAlias = ctx.GetParameterSymbol(acceptFn.Parameters[0])!.Type;
        var aliasOfSlice = Assert.IsType<AliasSemanticType>(pAlias.SpecialType);
        Assert.IsType<SliceSemanticType>(aliasOfSlice.TargetType.SpecialType);
        Assert.True(pAlias.IsSized(ctx));

        var pRef = ctx.GetParameterSymbol(acceptFn.Parameters[1])!.Type;
        var refOfSlice = Assert.IsType<ReferenceSemanticType>(pRef.SpecialType);
        Assert.IsType<SliceSemanticType>(refOfSlice.ElementType.SpecialType);
        Assert.True(pRef.IsSized(ctx));

        var pGrouped = ctx.GetParameterSymbol(acceptFn.Parameters[4])!.Type;
        var groupedAlias = Assert.IsType<AliasSemanticType>(pGrouped.SpecialType);
        var groupedSlice = Assert.IsType<SliceSemanticType>(groupedAlias.TargetType.SpecialType);
        Assert.IsType<AliasSemanticType>(groupedSlice.ElementType.SpecialType);
        Assert.True(pGrouped.IsSized(ctx));

        // Verify '(a) * b;' and '(a) & b;' parsed as expression statements rather than declarations.
        var disambigFn = Assert.Single(ctx.FunctionSymbols, f => f.Name.Text == "StatementsDisambiguation");
        var body = Assert.IsType<FunctionBlockBody>(disambigFn.Syntax!.Body);
        var exprStmts = body.Locals.OfType<LocalExpressionStatement>().ToList();
        Assert.Equal(2, exprStmts.Count);
        Assert.All(exprStmts, s => Assert.IsType<BinaryExpression>(s.Expression));
    }

    [Fact]
    public void CanonicalDyn_AliasAndRefEquivalence_BoxInterop_And_InvalidOrderings()
    {
        const string dynContractPrelude = """
            intrinsic attribute IntrinsicType(&Str name);
            intrinsic attribute IntrinsicFunction(&Str signature);
            intrinsic attribute OperatorFunction(&Str name);

            [IntrinsicType("Layout")] public interface Layout;
            [IntrinsicType("Sized")] public interface Sized : Layout;
            [IntrinsicType("Unsized")] public interface Unsized : Layout;

            [IntrinsicType("UInt8")] public struct uint8;
            [IntrinsicType("Int32")] public struct int32;
            [IntrinsicType("NUInt")] public struct nuint;

            [IntrinsicType("ValueType")] public struct ValType;
            [IntrinsicType("Struct")] public struct Struct : ValType;

            [IntrinsicType("Slice")] public struct Slice<T, D : int = 1>;
            [IntrinsicType("Str")] public struct Str { public uint8[] data; }
            [IntrinsicType("Metadata")] public struct Metadata<T> where T : Layout;

            public interface Deref<TReturn>
            {
                [OperatorFunction("Dereference")]
                func Deref(&this) -> TReturn;
            }

            public interface Arrow<TReturn>
            {
                [OperatorFunction("IndirectMemberAccess")]
                func Arrow(&this) -> TReturn;
            }

            [IntrinsicType("Reference")]
            public struct Reference<T> : Arrow<&T>, Deref<&T> where T : Layout
            {
                public nuint raw;
                public Metadata<T> metadata;
                [IntrinsicFunction("Reference.Deref()")] func Deref.Deref(&this) -> &T;
                func Arrow.Arrow(&this) -> &T { return *this; }
            }

            [IntrinsicType("MutableReference")]
            public struct MutableReference<T> : Arrow<mut &T>, Deref<mut &T> where T : Layout
            {
                public nuint raw;
                public Metadata<T> metadata;
                [IntrinsicFunction("MutableReference.Deref()")] func Deref.Deref(&this) -> mut &T;
                func Arrow.Arrow(&this) -> mut &T { return *this; }
            }

            [IntrinsicType("Pointer")]
            public struct Pointer<T> : Arrow<&T>, Deref<&T> where T : Layout
            {
                public nuint addr;
                public Metadata<T> metadata;
                [IntrinsicFunction("Pointer.Deref()")] func Deref.Deref(&this) -> &T;
                func Arrow.Arrow(&this) -> &T { return *this; }
            }

            [IntrinsicType("MutablePointer")]
            public struct MutablePointer<T> : Arrow<mut &T>, Deref<mut &T> where T : Layout
            {
                public nuint addr;
                public Metadata<T> metadata;
                [IntrinsicFunction("MutablePointer.Deref()")] func Deref.Deref(&this) -> mut &T;
                func Arrow.Arrow(&this) -> mut &T { return *this; }
            }

            public interface Allocator { func Allocate(mut &this, nuint size, nuint alignment) -> ValType*; }
            public struct GlobalAllocator
            {
                public static mut dyn Allocator mut* instance;
                public func Allocate(nuint size, nuint alignment) -> ValType* { return default; }
            }

            [IntrinsicType("Box")]
            public struct Box<T> : Arrow<&T>, Deref<&T> where T : Layout
            {
                public T mut& pointer;
                func Arrow.Arrow(&this) -> &T { return *pointer; }
                func Deref.Deref(&this) -> &T { return *pointer; }
            }

            public interface Callable<TReturn, TParams...>
            {
                [OperatorFunction("Callable")]
                func Invoke(&this, TParams args) -> TReturn;
            }

            [IntrinsicType("Function")]
            public struct Function<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
            {
                public nuint codeAddress;
                [IntrinsicFunction("Function.Invoke(TParams...)")] func Callable.Invoke(&this, TParams args) -> TReturn;
            }

            [IntrinsicType("Delegate")]
            public struct Delegate<TReturn, TParams...> : ValType, Callable<TReturn, TParams...>
            {
                public nuint codeAddress;
                public Pointer<ValType> env;
                [IntrinsicFunction("Delegate.Invoke(TParams...)")] func Callable.Invoke(&this, TParams args) -> TReturn;
            }
            """;

        var validSource = dynContractPrelude + """
            public interface IGreetable
            {
                func Greet(&this) -> int32;
            }

            public struct Greeter : IGreetable
            {
                public func Greet(&this) -> int32 { return 7; }
            }

            public func TakeAlias(dyn &IGreetable a) -> int32 { return a.Greet(); }
            public func TakeRef(dyn IGreetable& r) -> int32 { return r->Greet(); }
            public func TakeRefDeref(dyn IGreetable& r) -> int32 { return (*r).Greet(); }

            public func CrossConvert(dyn &IGreetable a, dyn IGreetable& r, dyn IGreetable* p, dyn IGreetable mut* mp) -> int32
            {
                dyn IGreetable& rFromA = a;
                dyn &IGreetable aFromR = *r;
                dyn &IGreetable aFromRImplicit = r;
                int32 x = TakeRef(a);
                int32 y = TakeAlias(r);
                Box<dyn IGreetable> boxed = default;
                dyn &IGreetable fromBox = *boxed;
                int32 z = boxed->Greet();
                return fromBox.Greet();
            }
            """;

        var validComp = Compilation.FromSource(validSource);
        Assert.False(validComp.HasErrors, string.Join("\n", validComp.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        string[] invalidTypes =
        [
            "&dyn IGreetable x",
            "mut &dyn IGreetable x",
            "*dyn IGreetable x",
            "(dyn IGreetable)& x",
            "(dyn IGreetable)* x"
        ];

        foreach (var decl in invalidTypes)
        {
            var invalidSource = dynContractPrelude + $$"""
                public interface IGreetable
                {
                    func Greet(&this) -> int32;
                }

                public func Test({{decl}}) -> ()
                {
                }
                """;

            var comp = Compilation.FromSource(invalidSource);
            Assert.True(comp.HasErrors, $"Expected MH0583 for: {decl}");
            Assert.Contains(comp.Diagnostics, d => d.Code == "MH0583");
        }
    }

    [Fact]
    public void TailUnsizedStruct_ValidAndInvalidPlacements_EnforcedByMH0580AndMH0582()
    {
        var validSource = Prelude + """
            public struct Packet
            {
                public int32 length;
                public uint8[] data;
            }

            public struct OuterPacket
            {
                public int32 tag;
                public Packet inner;
            }

            public struct ZeroFieldDerived : Packet;

            public func Inspect(&Packet p, Packet& r, &OuterPacket op, &ZeroFieldDerived zd) -> uint8
            {
                return p.data[0];
            }
            """;

        var validComp = Compilation.FromSource(validSource);
        Assert.False(validComp.HasErrors, string.Join("\n", validComp.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var ctx = validComp.Context!;
        var packetSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "Packet");
        var outerSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "OuterPacket");
        var derivedSym = Assert.Single(ctx.TypeSymbols, t => t.Name.Text == "ZeroFieldDerived");
        Assert.False(TypeRef.Resolved(ResolutionContext.GetHandle(packetSym)).IsSized(ctx));
        Assert.False(TypeRef.Resolved(ResolutionContext.GetHandle(outerSym)).IsSized(ctx));
        Assert.False(TypeRef.Resolved(ResolutionContext.GetHandle(derivedSym)).IsSized(ctx));

        var invalidSource = Prelude + """
            public struct Packet : Throwable
            {
                public int32 length;
                public uint8[] data;
            }

            public struct SecondUnsized
            {
                public uint8[] bytes;
            }

            public struct BadNonTail
            {
                public uint8[] head;
                public int32 tail;
            }

            public struct BadTransitiveNonTail
            {
                public Packet p;
                public int32 after;
            }

            public struct BadWithStatic
            {
                public static Packet shared;
            }

            public struct BadDerivedAddsField : Packet
            {
                public int32 extra;
            }

            public struct BadMultipleUnsizedBases : Packet, SecondUnsized;

            public enum BadEnum
            {
                Payload(Packet p);
            }

            public static Str gStr;
            public const Packet cPkt = default;

            public func BadSignatures(Str s) -> Packet
            {
                Packet local;
                (Packet | int32) badUnion;
                var d = default(Str);
                var p = put Packet { length = 1, data = default };
                try
                {
                }
                catch (Str e)
                {
                }
            }
            """;

        var invalidComp = Compilation.FromSource(invalidSource);
        Assert.True(invalidComp.HasErrors);
        Assert.True(invalidComp.Diagnostics.Count(d => d.Code == "MH0580") >= 9);
        Assert.Equal(2, invalidComp.Diagnostics.Count(d => d.Code == "MH0582"));
    }

    [Fact]
    public void BareInterface_RejectedOutsideConstraintsAndDyn_WhileConstraintAndConformanceAllowed()
    {
        var source = Prelude + """
            public interface IRun
            {
                func Run(&this) -> int32;
            }

            public struct Runner : IRun
            {
                public func Run(&this) -> int32 { return 1; }
            }

            public struct Holder<T> where T : Layout
            {
                public int32 tag;
            }

            public func ValidConstraint<T : IRun>(&T item) -> int32 { return 1; }
            public func ValidHolder(Holder<dyn &IRun> h) -> () {}
            public func ValidBox(Box<dyn IRun> b) -> int32 { return b->Run(); }
            public func ValidConformance(Runner r) -> int32 { return (r as IRun).Run(); }

            public func BadBare(IRun v, &IRun a, mut &IRun ma, IRun& r, IRun[] s, Holder<IRun> h, Box<IRun> b) -> IRun
            {
                IRun local;
                return local;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.True(compilation.Diagnostics.Count(d => d.Code == "MH0583") >= 9);
    }

    [Fact]
    public void GenericConstraints_DefaultSized_Layout_Unsized_And_UnsizingCoercions()
    {
        var validAndCoercionSource = Prelude + """
            public struct Wrapper<T> where T : Layout
            {
                public int32 tag;
                public T payload;
            }

            public func RequireSized<T>(&T v) -> int32 { return 1; }
            public func AllowAnyLayout<T>(&T v) -> int32 where T : Layout { return 2; }
            public func RequireUnsized<T>(&T v) -> int32 where T : Unsized { return 3; }

            public extension<T> Wrapper<T> where T : Sized
            {
                public func ExtractSized(&this) -> T { return this.payload; }
            }

            public func TakeByteSlice(&uint8[] s) -> int32 { return 1; }
            public func TakeByteSliceRef(uint8[]& s) -> int32 { return 2; }
            public func TakeUnsizedWrapper(&Wrapper<uint8[]> w) -> int32 { return 3; }

            public func Test(uint8[4]& fixedArr, Wrapper<uint8[4]>& wrappedArr, &Wrapper<int32> boxedNum, &Str text, &int32 num) -> int32
            {
                int32 a = RequireSized::<int32>(num);
                int32 b = AllowAnyLayout::<int32>(num);
                int32 c = AllowAnyLayout::<Str>(text);
                int32 d = AllowAnyLayout::<uint8[4]>(fixedArr);
                int32 e = RequireUnsized::<Str>(text);
                int32 f = boxedNum.ExtractSized();

                &uint8[] sliceAlias = fixedArr;
                uint8[]& sliceRef = fixedArr;
                &Wrapper<uint8[]> wAlias = wrappedArr;

                int32 s1 = TakeByteSlice(fixedArr);
                int32 s2 = TakeByteSliceRef(fixedArr);
                return TakeUnsizedWrapper(wrappedArr);
            }
            """;

        var validComp = Compilation.FromSource(validAndCoercionSource);
        Assert.False(validComp.HasErrors, string.Join("\n", validComp.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var invalidSource = Prelude + """
            public interface SizedOnly : Sized
            {
                func SizeTag(&this) -> int32;
            }

            public struct BadUnsizedImpl : SizedOnly
            {
                public uint8[] bytes;
                public func SizeTag(&this) -> int32 { return 0; }
            }

            public func NeedSized<T>(&T x) -> int32 { return 1; }
            public func NeedUnsized<T>(&T x) -> int32 where T : Unsized { return 2; }

            public func ForwardLayout<T>(&T x) -> int32 where T : Layout
            {
                int32 a = NeedSized::<T>(x);
                int32 b = NeedUnsized::<T>(x);
                return a;
            }

            public func TestConcrete(&Str text, &int32 num) -> ()
            {
                NeedSized::<Str>(text);
                NeedUnsized::<int32>(num);
            }
            """;

        var invalidComp = Compilation.FromSource(invalidSource);
        Assert.True(invalidComp.HasErrors);
        Assert.Equal(5, invalidComp.Diagnostics.Count(d => d.Code == "MH0581"));
    }

    [Fact]
    public void Main_EntryPoint_ValidUnsizedStrSliceSignatures_Succeed_AndRawStrSliceFails()
    {
        string[] validMainSignatures =
        [
            "public func Main(&(&Str)[] args) -> int32 { return 0; }",
            "public func Main((&Str)[]& args) -> int32 { return 0; }",
            "public func Main(dyn &((&Str)[]) args) -> int32 { return 0; }",
            "public func Main(dyn (&Str)[]& args) -> int32 { return 0; }"
        ];

        foreach (var sig in validMainSignatures)
        {
            var comp = Compilation.FromSource(Prelude + "\n" + sig);
            Assert.False(comp.HasErrors, $"{sig}: " + string.Join("; ", comp.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
            Assert.NotNull(comp.Context.EntryPoint);
        }

        var invalidComp = Compilation.FromSource(Prelude + """
            public func Main(Str[] args) -> int32 { return 0; }
            """);
        Assert.True(invalidComp.HasErrors);
        Assert.Contains(invalidComp.Diagnostics, d => d.Code is "MH0580" or "MH0163");
    }
}
