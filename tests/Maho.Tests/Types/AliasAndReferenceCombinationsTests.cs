using Maho.Resolution;
using Maho.Syntax;

namespace Maho.Tests;

public sealed class AliasAndReferenceCombinationsTests
{
    private const string Prelude = """
        intrinsic attribute IntrinsicType(Str name);
        intrinsic attribute IntrinsicFunction(Str signature);
        intrinsic attribute OperatorFunction(Str name);

        [IntrinsicType("Int8")]
        public struct int8;
        [IntrinsicType("Int32")]
        public struct Int32;
        [IntrinsicType("NUInt")]
        public struct nuint;
        [IntrinsicType("Never")]
        public struct Never;

        [IntrinsicType("Metadata")]
        public struct Metadata<T>;

        public interface Arrow<TReturn>
        {
            [OperatorFunction("IndirectMemberAccess")]
            public func Arrow(&this) -> TReturn;
        }

        public interface Deref<TReturn>
        {
            [OperatorFunction("Dereference")]
            public func Deref(&this) -> TReturn;
        }

        public interface Addr<T>
        {
            [OperatorFunction("AddressOf")]
            public func AddressOf(&this) -> T*;
        }

        [IntrinsicType("Reference")]
        public struct Reference<T> : Arrow<&T>, Deref<&T>
        {
            nuint raw;
            Metadata<T> metadata;

            [IntrinsicFunction("Reference.Deref()")]
            public func Deref.Deref(&this) -> &T;

            public func Arrow.Arrow(&this) -> &T
            {
                return *this;
            }
        }

        [IntrinsicType("MutableReference")]
        public struct MutableReference<T> : Arrow<mut &T>, Deref<mut &T>
        {
            nuint raw;
            Metadata<T> metadata;

            [IntrinsicFunction("MutableReference.Deref()")]
            public func Deref.Deref(&this) -> mut &T;

            public func Arrow.Arrow(&this) -> mut &T
            {
                return *this;
            }
        }

        [IntrinsicType("Pointer")]
        public struct Pointer<T> : Arrow<&T>, Deref<&T>
        {
            nuint addr;
            Metadata<T> metadata;

            [IntrinsicFunction("Pointer.Deref()")]
            public func Deref.Deref(&this) -> &T;

            public func Arrow.Arrow(&this) -> &T
            {
                return *this;
            }
        }

        [IntrinsicType("MutablePointer")]
        public struct MutablePointer<T> : Arrow<mut &T>, Deref<mut &T>
        {
            nuint addr;
            Metadata<T> metadata;

            [IntrinsicFunction("MutablePointer.Deref()")]
            public func Deref.Deref(&this) -> mut &T;

            public func Arrow.Arrow(&this) -> mut &T
            {
                return *this;
            }
        }

        [IntrinsicType("Span")]
        public struct Span<T>
        {
            T& data;
            nuint len;
        }

        [IntrinsicType("Array")]
        public struct Array<T>
        {
            T* data;
            nuint len;
        }

        [IntrinsicType("FixedArray")]
        public struct FixedArray<T, N>;

        [IntrinsicType("Str")]
        public struct Str
        {
            int8& buffer;
            nuint length;
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

        public struct Point : Addr<Point>
        {
            public Int32 x;
            public Int32 y;

            public func Addr.AddressOf(&this) -> Point*
            {
                Point* p = default;
                return p;
            }
        }
        """;

    [Fact]
    public void UninitializedSafeAlias_ReportsMH0125()
    {
        var source = Prelude + """
            public func Test()
            {
                &Point p;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0125");
    }

    [Fact]
    public void UninitializedUnsafeAlias_ReportsMH0125()
    {
        var source = Prelude + """
            public func Test()
            {
                *Point p;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0125");
    }

    [Fact]
    public void InitializedSafeAlias_DirectDotAccess_Succeeds()
    {
        var source = Prelude + """
            public func Test(Point pt) -> Int32
            {
                &Point p = pt;
                return p.x;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void InitializedUnsafeAlias_DirectDotAccess_Succeeds()
    {
        var source = Prelude + """
            public func Test(Point pt) -> Int32
            {
                *Point p = pt;
                return p.y;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void ImmutableAlias_Reassignment_FailsWithMH2009()
    {
        var source = Prelude + """
            public func Test(Point pt1, Point pt2)
            {
                &Point p = pt1;
                p = pt2;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH2009");
    }

    [Fact]
    public void MutableAlias_Reassignment_Succeeds()
    {
        var source = Prelude + """
            public func Test(Point pt1, Point pt2)
            {
                mut &Point p = pt1;
                p = pt2;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void AddressOf_OnAlias_InvokesOperatorOnUnderlyingType()
    {
        var source = Prelude + """
            public func Test(Point pt) -> Point*
            {
                &Point p = pt;
                return &p;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void FourReferenceCombinations_DeclarationAndResolution_Succeeds()
    {
        var source = Prelude + """
            public struct RefHolder
            {
                public func TestRefs(
                    Int32& r1,
                    mut Int32& r2,
                    Int32 mut& r3,
                    mut Int32 mut& r4
                ) -> ()
                {
                    r2 = r1;
                    r4 = r3;
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void AllEightReceiverKinds_DeclarationAndParse_Succeeds()
    {
        var source = Prelude + """
            public struct ReceiverMatrix
            {
                public Int32 val;

                public func F_Val(this) -> Int32 { return this.val; }
                public func F_Alias(&this) -> Int32 { return this.val; }
                public func F_MutAlias(mut &this) -> Int32 { return this.val; }
                public func F_UnsafeAlias(*this) -> Int32 { return this.val; }
                public func F_MutUnsafeAlias(mut *this) -> Int32 { return this.val; }
                public func F_Ref(this&) -> Int32 { return this->val; }
                public func F_MutRef(this mut&) -> Int32 { return this->val; }
                public func F_Ptr(this*) -> Int32 { return this->val; }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void SpanTypeModifier_PostfixAmpersandBracket_ResolvesToSpan()
    {
        var source = Prelude + """
            public struct Container
            {
                public Int32[&] s;
                public Int32[] a;
                public Int32[10] fa;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var container = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.ToString() == "Container");
        var containerHandle = ResolutionContext.GetHandle(container);
        var fields = compilation.Context.FieldSymbols.Where(f => f.Parent == containerHandle).ToList();
        var sField = Assert.Single(fields, f => f.Name.ToString() == "s");
        var aField = Assert.Single(fields, f => f.Name.ToString() == "a");
        var faField = Assert.Single(fields, f => f.Name.ToString() == "fa");

        Assert.Equal(TypeRefKind.Span, sField.Type.Kind);
        Assert.Equal(TypeRefKind.Array, aField.Type.Kind);
        Assert.Equal(TypeRefKind.FixedArray, faField.Type.Kind);
        Assert.Equal("Int32[&]", sField.Type.ToDisplayString(compilation.Context));
        Assert.Equal("Int32[]", aField.Type.ToDisplayString(compilation.Context));
        var fixedArr = Assert.IsType<FixedArraySemanticType>(faField.Type.SpecialType);
        Assert.Equal("10", Assert.IsType<LiteralExpression>(fixedArr.Size).Literal.Value);
    }

    [Fact]
    public void Never_HasZeroBases_AndNoInheritedValueType()
    {
        var compilation = Compilation.FromSource(Prelude);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var neverSymbol = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.ToString() == "Never");
        Assert.Empty(neverSymbol.BaseTypes);

        var valTypeSymbol = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.ToString() == "ValType");
        var structSymbol = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.ToString() == "Struct");
        var valTypeHandle = ResolutionContext.GetHandle(valTypeSymbol);
        Assert.Contains(structSymbol.BaseTypes, b => b.Handle == valTypeHandle);
    }

    [Fact]
    public void UniversalLValue_ExpressionReturningMutableAlias_CanBeAssignedDirectly()
    {
        var source = Prelude + """
            public struct Entity
            {
                public Int32 id;
            }

            public struct Container
            {
                public Entity entity;

                public func GetMut(&this) -> mut &Entity
                {
                    return entity;
                }
            }

            public func Test()
            {
                Container c = default;
                Entity e = default;
                c.GetMut() = e;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void PrefixModifier_MutSafeAndUnsafeAlias_InGenericArguments()
    {
        var source = Prelude + """
            public interface Getter<T>
            {
                public func Get() -> T;
            }

            public struct EntityProvider : Getter<mut &Point>
            {
                public Point pt;
                public func Getter.Get() -> mut &Point
                {
                    return pt;
                }
            }

            public struct UnsafeProvider : Getter<mut *Point>
            {
                public Point pt;
                public func Getter.Get() -> mut *Point
                {
                    return pt;
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void MutablePointer_TMutStar_AndThisMutStarReceiver()
    {
        var source = Prelude + """
            public struct Entity
            {
                public Int32 id;

                public func Inspect(this mut*) -> nuint
                {
                    nuint addr = 0;
                    return addr;
                }
            }

            public func Test()
            {
                Entity e;
                Entity mut* ptr;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Char_IntrinsicType_AndSuffixedLiteral()
    {
        var source = Prelude + """
            [IntrinsicType("Char")]
            public struct Char;

            public struct ByteChar
            {
                public int8 val;

                public literal u8(int8 b)
                {
                    ByteChar bc = default;
                    return bc;
                }
            }

            public func Test()
            {
                Char c;
                var lit = 'a'u8;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var charSymbol = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.ToString() == "Char");
        var structSymbol = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.ToString() == "Struct");
        var structHandle = ResolutionContext.GetHandle(structSymbol);
        Assert.Contains(charSymbol.BaseTypes, b => b.Handle == structHandle);
    }

    [Fact]
    public void StrAndSpan_DeriveFromStruct_AndOptional_DerivesFromEnum()
    {
        var source = Prelude + """
            [IntrinsicType("Enum")]
            public struct Enum : ValType;

            [IntrinsicType("Optional")]
            public enum Optional<T>
            {
                Some(T value),
                None;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        var strSym = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.ToString() == "Str");
        var spanSym = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.ToString() == "Span");
        var optSym = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.ToString() == "Optional");
        var structSym = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.ToString() == "Struct");
        var enumSym = Assert.Single(compilation.Context.TypeSymbols, s => s.Name.ToString() == "Enum");

        var structHandle = ResolutionContext.GetHandle(structSym);
        var enumHandle = ResolutionContext.GetHandle(enumSym);

        Assert.Contains(strSym.BaseTypes, b => b.Handle == structHandle);
        Assert.Contains(spanSym.BaseTypes, b => b.Handle == structHandle);
        Assert.Contains(optSym.BaseTypes, b => b.Handle == enumHandle);
    }

    [Fact]
    public void SelfReferential_TypeModifier_InsideIntrinsicDeclaration()
    {
        var source = Prelude + """
            [IntrinsicType("Enum")]
            public struct Enum : ValType;

            [IntrinsicType("Optional")]
            public enum Optional<T>
            {
                Some(T value),
                None;

                public func AsOptional(this) -> T?
                {
                    return this;
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Deref_And_Arrow_Interfaces_With_Receiver_Parameters()
    {
        var source = Prelude + """
            public struct Item
            {
                public Int32 value;
            }

            public func TestDerefAndArrow(Item* ro, Item mut* rw)
            {
                &Item a = *ro;
                mut &Item b = *rw;
                *rw = a;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Receiver_Kind_Mismatch_Between_Interface_And_Implementation_Reports_MH0550()
    {
        var source = Prelude + """
            public interface IFoo
            {
                public func Bar(&this) -> Int32;
            }

            public struct MyFoo : IFoo
            {
                public func Bar(mut &this) -> Int32
                {
                    Int32 i;
                    return i;
                }
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.True(compilation.HasErrors);
        Assert.Contains(compilation.Diagnostics, d => d.Code == "MH0550");
    }

    [Fact]
    public void Clear_Comparison_Operator_Names_And_Contracts()
    {
        var source = Prelude + """
            [IntrinsicType("Bool")]
            public struct Bool;

            public interface LessThanOrEqual<TResult, TLeft, TRight>
            {
                [OperatorFunction("LessThanOrEqual")]
                public func LessThanOrEqual(TLeft lhs, TRight rhs) -> TResult;
            }

            public interface GreaterThanOrEqual<TResult, TLeft, TRight>
            {
                [OperatorFunction("GreaterThanOrEqual")]
                public func GreaterThanOrEqual(TLeft lhs, TRight rhs) -> TResult;
            }

            public struct Number : LessThanOrEqual<Bool, Number, Number>,
                                  GreaterThanOrEqual<Bool, Number, Number>
            {
                public func LessThanOrEqual.LessThanOrEqual(Number lhs, Number rhs) -> Bool
                {
                    Bool b = default;
                    return b;
                }

                public func GreaterThanOrEqual.GreaterThanOrEqual(Number lhs, Number rhs) -> Bool
                {
                    Bool b = default;
                    return b;
                }
            }

            public func Compare(Number a, Number b) -> Bool
            {
                return a <= b;
            }
            """;

        var compilation = Compilation.FromSource(source);
        Assert.False(compilation.HasErrors, string.Join("\n", compilation.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }
}
