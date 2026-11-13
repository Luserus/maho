using System;
using System.Collections.Generic;

namespace Maho.Resolution;

internal static class KnownIntrinsics
{
    public const string IntrinsicTypeAttribute = "IntrinsicType";
    public const string IntrinsicFunctionAttribute = "IntrinsicFunction";
    public const string OperatorFunctionAttribute = "OperatorFunction";

    // Signed Integers
    public const string Int8 = "Int8";
    public const string Int16 = "Int16";
    public const string Int32 = "Int32";
    public const string Int64 = "Int64";
    public const string Int128 = "Int128";
    public const string NInt = "NInt";

    // Unsigned Integers
    public const string UInt8 = "UInt8";
    public const string UInt16 = "UInt16";
    public const string UInt32 = "UInt32";
    public const string UInt64 = "UInt64";
    public const string UInt128 = "UInt128";
    public const string NUInt = "NUInt";

    // Floating-Point
    public const string Float16 = "Float16";
    public const string Float32 = "Float32";
    public const string Float64 = "Float64";
    public const string Float128 = "Float128";

    // Primitives
    public const string Bool = "Bool";
    public const string Char = "Char";
    public const string Str = "Str";
    public const string Unit = "Unit";

    // Value types and Callables
    public const string ValueType = "ValueType";
    public const string Struct = "Struct";
    public const string Enum = "Enum";
    public const string Function = "Function";
    public const string Delegate = "Delegate";
    public const string Tuple = "Tuple";

    // Pointer and Reference
    public const string Pointer = "Pointer";
    public const string Reference = "Reference";

    // Arrays and Spans
    public const string FixedArray = "FixedArray";
    public const string Array = "Array";
    public const string Span = "Span";

    // Operator Full Names
    public const string OpAddition = "Addition";
    public const string OpSubtraction = "Subtraction";
    public const string OpMultiplication = "Multiplication";
    public const string OpDivision = "Division";
    public const string OpRemainder = "Remainder";
    public const string OpBitwiseAnd = "BitwiseAnd";
    public const string OpBitwiseOr = "BitwiseOr";
    public const string OpBitwiseXor = "BitwiseXor";
    public const string OpLeftShift = "LeftShift";
    public const string OpRightShift = "RightShift";
    public const string OpRotateLeft = "RotateLeft";
    public const string OpRotateRight = "RotateRight";
    public const string OpEquality = "Equality";
    public const string OpInequality = "Inequality";
    public const string OpLessThan = "LessThan";
    public const string OpLessThanOrEqual = "LessThanOrEqual";
    public const string OpGreaterThan = "GreaterThan";
    public const string OpGreaterThanOrEqual = "GreaterThanOrEqual";
    public const string OpLogicalAnd = "LogicalAnd";
    public const string OpLogicalOr = "LogicalOr";

    public const string OpUnaryNegation = "UnaryNegation";
    public const string OpUnaryPositive = "UnaryPositive";
    public const string OpLogicalNot = "LogicalNot";
    public const string OpBitwiseNot = "BitwiseNot";
    public const string OpPrefixIncrement = "PrefixIncrement";
    public const string OpPostfixIncrement = "PostfixIncrement";
    public const string OpPrefixDecrement = "PrefixDecrement";
    public const string OpPostfixDecrement = "PostfixDecrement";

    public const string OpAddressOf = "AddressOf";
    public const string OpDereference = "Dereference";
    public const string OpIndirectMemberAccess = "IndirectMemberAccess";
    public const string OpIndexerGetter = "IndexerGetter";
    public const string OpIndexerSetter = "IndexerSetter";

    public const string OpAssignment = "Assignment";
    public const string OpAdditionAssignment = "AdditionAssignment";
    public const string OpSubtractionAssignment = "SubtractionAssignment";
    public const string OpMultiplicationAssignment = "MultiplicationAssignment";
    public const string OpDivisionAssignment = "DivisionAssignment";
    public const string OpRemainderAssignment = "RemainderAssignment";
    public const string OpBitwiseAndAssignment = "BitwiseAndAssignment";
    public const string OpBitwiseOrAssignment = "BitwiseOrAssignment";
    public const string OpBitwiseXorAssignment = "BitwiseXorAssignment";
    public const string OpLeftShiftAssignment = "LeftShiftAssignment";
    public const string OpRightShiftAssignment = "RightShiftAssignment";
    public const string OpRotateLeftAssignment = "RotateLeftAssignment";
    public const string OpRotateRightAssignment = "RotateRightAssignment";

    public const string OpConstructor = "Constructor";
    public const string OpDestructor = "Destructor";
    public const string OpImplicitCast = "ImplicitCast";
    public const string OpExplicitCast = "ExplicitCast";
    public const string OpNew = "New";
    public const string OpPut = "Put";
    public const string OpCopy = "Copy";
    public const string OpMove = "Move";
    public const string OpCallable = "Callable";

    public static readonly HashSet<string> AllKnownTypes =
    [
        Int8, Int16, Int32, Int64, Int128, NInt,
        UInt8, UInt16, UInt32, UInt64, UInt128, NUInt,
        Float16, Float32, Float64, Float128,
        Bool, Char, Str, Unit,
        ValueType, Struct, Enum, Function, Delegate, Tuple,
        Pointer, Reference,
        FixedArray, Array, Span
    ];

    public static readonly HashSet<string> ZeroInstanceFieldPrimitives =
    [
        Int8, Int16, Int32, Int64, Int128, NInt,
        UInt8, UInt16, UInt32, UInt64, UInt128, NUInt,
        Float16, Float32, Float64, Float128,
        Bool, Char, Unit,
        ValueType, Struct, Enum, Tuple,
        FixedArray
    ];

    public static readonly HashSet<string> AllOperatorNames =
    [
        OpAddition, OpSubtraction, OpMultiplication, OpDivision, OpRemainder,
        OpBitwiseAnd, OpBitwiseOr, OpBitwiseXor, OpLeftShift, OpRightShift,
        OpRotateLeft, OpRotateRight, OpEquality, OpInequality,
        OpLessThan, OpLessThanOrEqual, OpGreaterThan, OpGreaterThanOrEqual,
        OpLogicalAnd, OpLogicalOr,
        OpUnaryNegation, OpUnaryPositive, OpLogicalNot, OpBitwiseNot,
        OpPrefixIncrement, OpPostfixIncrement, OpPrefixDecrement, OpPostfixDecrement,
        OpAddressOf, OpDereference, OpIndirectMemberAccess,
        OpIndexerGetter, OpIndexerSetter,
        OpAssignment,
        OpAdditionAssignment, OpSubtractionAssignment, OpMultiplicationAssignment,
        OpDivisionAssignment, OpRemainderAssignment,
        OpBitwiseAndAssignment, OpBitwiseOrAssignment, OpBitwiseXorAssignment,
        OpLeftShiftAssignment, OpRightShiftAssignment,
        OpRotateLeftAssignment, OpRotateRightAssignment,
        OpConstructor, OpDestructor, OpImplicitCast, OpExplicitCast,
        OpNew, OpPut, OpCopy, OpMove, OpCallable
    ];

    public static bool TryGetOperatorKind(ReadOnlySpan<char> name, out OperatorKind kind)
    {
        switch (name)
        {
            case OpAddition: kind = OperatorKind.Addition; return true;
            case OpSubtraction: kind = OperatorKind.Subtraction; return true;
            case OpMultiplication: kind = OperatorKind.Multiplication; return true;
            case OpDivision: kind = OperatorKind.Division; return true;
            case OpRemainder: kind = OperatorKind.Remainder; return true;
            case OpBitwiseAnd: kind = OperatorKind.BitwiseAnd; return true;
            case OpBitwiseOr: kind = OperatorKind.BitwiseOr; return true;
            case OpBitwiseXor: kind = OperatorKind.BitwiseXor; return true;
            case OpLeftShift: kind = OperatorKind.LeftShift; return true;
            case OpRightShift: kind = OperatorKind.RightShift; return true;
            case OpRotateLeft: kind = OperatorKind.RotateLeft; return true;
            case OpRotateRight: kind = OperatorKind.RotateRight; return true;
            case OpEquality: kind = OperatorKind.Equality; return true;
            case OpInequality: kind = OperatorKind.Inequality; return true;
            case OpLessThan: kind = OperatorKind.LessThan; return true;
            case OpLessThanOrEqual: kind = OperatorKind.LessThanOrEqual; return true;
            case OpGreaterThan: kind = OperatorKind.GreaterThan; return true;
            case OpGreaterThanOrEqual: kind = OperatorKind.GreaterThanOrEqual; return true;
            case OpLogicalAnd: kind = OperatorKind.LogicalAnd; return true;
            case OpLogicalOr: kind = OperatorKind.LogicalOr; return true;
            case OpUnaryNegation: kind = OperatorKind.UnaryNegation; return true;
            case OpUnaryPositive: kind = OperatorKind.UnaryPositive; return true;
            case OpLogicalNot: kind = OperatorKind.LogicalNot; return true;
            case OpBitwiseNot: kind = OperatorKind.BitwiseNot; return true;
            case OpPrefixIncrement: kind = OperatorKind.PrefixIncrement; return true;
            case OpPostfixIncrement: kind = OperatorKind.PostfixIncrement; return true;
            case OpPrefixDecrement: kind = OperatorKind.PrefixDecrement; return true;
            case OpPostfixDecrement: kind = OperatorKind.PostfixDecrement; return true;
            case OpAddressOf: kind = OperatorKind.AddressOf; return true;
            case OpDereference: kind = OperatorKind.Dereference; return true;
            case OpIndirectMemberAccess: kind = OperatorKind.IndirectMemberAccess; return true;
            case OpIndexerGetter: kind = OperatorKind.IndexerGetter; return true;
            case OpIndexerSetter: kind = OperatorKind.IndexerSetter; return true;
            case OpAssignment: kind = OperatorKind.Assignment; return true;
            case OpAdditionAssignment: kind = OperatorKind.AdditionAssignment; return true;
            case OpSubtractionAssignment: kind = OperatorKind.SubtractionAssignment; return true;
            case OpMultiplicationAssignment: kind = OperatorKind.MultiplicationAssignment; return true;
            case OpDivisionAssignment: kind = OperatorKind.DivisionAssignment; return true;
            case OpRemainderAssignment: kind = OperatorKind.RemainderAssignment; return true;
            case OpBitwiseAndAssignment: kind = OperatorKind.BitwiseAndAssignment; return true;
            case OpBitwiseOrAssignment: kind = OperatorKind.BitwiseOrAssignment; return true;
            case OpBitwiseXorAssignment: kind = OperatorKind.BitwiseXorAssignment; return true;
            case OpLeftShiftAssignment: kind = OperatorKind.LeftShiftAssignment; return true;
            case OpRightShiftAssignment: kind = OperatorKind.RightShiftAssignment; return true;
            case OpRotateLeftAssignment: kind = OperatorKind.RotateLeftAssignment; return true;
            case OpRotateRightAssignment: kind = OperatorKind.RotateRightAssignment; return true;
            case OpConstructor: kind = OperatorKind.Constructor; return true;
            case OpDestructor: kind = OperatorKind.Destructor; return true;
            case OpImplicitCast: kind = OperatorKind.ImplicitCast; return true;
            case OpExplicitCast: kind = OperatorKind.ExplicitCast; return true;
            case OpNew: kind = OperatorKind.New; return true;
            case OpPut: kind = OperatorKind.Put; return true;
            case OpCopy: kind = OperatorKind.Copy; return true;
            case OpMove: kind = OperatorKind.Move; return true;
            case OpCallable: kind = OperatorKind.Callable; return true;
            default:
                kind = OperatorKind.None;
                return false;
        }
    }

    public static string GetOperatorName(OperatorKind kind) => kind switch
    {
        OperatorKind.Addition => OpAddition,
        OperatorKind.Subtraction => OpSubtraction,
        OperatorKind.Multiplication => OpMultiplication,
        OperatorKind.Division => OpDivision,
        OperatorKind.Remainder => OpRemainder,
        OperatorKind.BitwiseAnd => OpBitwiseAnd,
        OperatorKind.BitwiseOr => OpBitwiseOr,
        OperatorKind.BitwiseXor => OpBitwiseXor,
        OperatorKind.LeftShift => OpLeftShift,
        OperatorKind.RightShift => OpRightShift,
        OperatorKind.RotateLeft => OpRotateLeft,
        OperatorKind.RotateRight => OpRotateRight,
        OperatorKind.Equality => OpEquality,
        OperatorKind.Inequality => OpInequality,
        OperatorKind.LessThan => OpLessThan,
        OperatorKind.LessThanOrEqual => OpLessThanOrEqual,
        OperatorKind.GreaterThan => OpGreaterThan,
        OperatorKind.GreaterThanOrEqual => OpGreaterThanOrEqual,
        OperatorKind.LogicalAnd => OpLogicalAnd,
        OperatorKind.LogicalOr => OpLogicalOr,
        OperatorKind.UnaryNegation => OpUnaryNegation,
        OperatorKind.UnaryPositive => OpUnaryPositive,
        OperatorKind.LogicalNot => OpLogicalNot,
        OperatorKind.BitwiseNot => OpBitwiseNot,
        OperatorKind.PrefixIncrement => OpPrefixIncrement,
        OperatorKind.PostfixIncrement => OpPostfixIncrement,
        OperatorKind.PrefixDecrement => OpPrefixDecrement,
        OperatorKind.PostfixDecrement => OpPostfixDecrement,
        OperatorKind.AddressOf => OpAddressOf,
        OperatorKind.Dereference => OpDereference,
        OperatorKind.IndirectMemberAccess => OpIndirectMemberAccess,
        OperatorKind.IndexerGetter => OpIndexerGetter,
        OperatorKind.IndexerSetter => OpIndexerSetter,
        OperatorKind.Assignment => OpAssignment,
        OperatorKind.AdditionAssignment => OpAdditionAssignment,
        OperatorKind.SubtractionAssignment => OpSubtractionAssignment,
        OperatorKind.MultiplicationAssignment => OpMultiplicationAssignment,
        OperatorKind.DivisionAssignment => OpDivisionAssignment,
        OperatorKind.RemainderAssignment => OpRemainderAssignment,
        OperatorKind.BitwiseAndAssignment => OpBitwiseAndAssignment,
        OperatorKind.BitwiseOrAssignment => OpBitwiseOrAssignment,
        OperatorKind.BitwiseXorAssignment => OpBitwiseXorAssignment,
        OperatorKind.LeftShiftAssignment => OpLeftShiftAssignment,
        OperatorKind.RightShiftAssignment => OpRightShiftAssignment,
        OperatorKind.RotateLeftAssignment => OpRotateLeftAssignment,
        OperatorKind.RotateRightAssignment => OpRotateRightAssignment,
        OperatorKind.Constructor => OpConstructor,
        OperatorKind.Destructor => OpDestructor,
        OperatorKind.ImplicitCast => OpImplicitCast,
        OperatorKind.ExplicitCast => OpExplicitCast,
        OperatorKind.New => OpNew,
        OperatorKind.Put => OpPut,
        OperatorKind.Copy => OpCopy,
        OperatorKind.Move => OpMove,
        OperatorKind.Callable => OpCallable,
        _ => string.Empty
    };
}

