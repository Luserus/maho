using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Maho.Syntax;

namespace Maho.Resolution.Passes;

/// <summary>
/// Evaluates compile-time constant expressions composed of literal values and immutable operations
/// before Control Flow Graph (CFG) construction and reachability analysis.
/// </summary>
internal sealed class ConstantFoldingPass : ResolutionPass
{
    [ThreadStatic]
    private static FunctionSymbol? currentFunction;
    [ThreadStatic]
    private static MethodSymbol? currentMethod;
    [ThreadStatic]
    private static int constContextDepth;
    [ThreadStatic]
    private static HashSet<BinaryExpression>? reportedWarnings;

    public override void Resolve(ResolutionContext context)
    {
        reportedWarnings = [];
        try
        {
            // 1. Evaluate globals
            foreach (var global in context.GlobalVariableSymbols)
            {
                if (global.Syntax?.Declarator.Initializer?.Initializer is { } initExpr)
                {
                    bool isConst = global.Flags.HasFlag(VariableFlags.Const);
                    if (isConst) constContextDepth++;
                    try { Evaluate(initExpr, context); }
                    finally { if (isConst) constContextDepth--; }
                }
            }

            // 2. Evaluate fields
            foreach (var field in context.FieldSymbols)
            {
                if (field.Syntax?.Declarator.Initializer?.Initializer is { } initExpr)
                {
                    bool isConst = field.Flags.HasFlag(VariableFlags.Const);
                    if (isConst) constContextDepth++;
                    try { Evaluate(initExpr, context); }
                    finally { if (isConst) constContextDepth--; }
                }
            }

            // 3. Evaluate functions
            foreach (var fn in context.FunctionSymbols)
            {
                if (fn.Syntax?.Body is { } body)
                {
                    currentFunction = fn;
                    currentMethod = null;
                    try
                    {
                        EvaluateBody(body, context);
                    }
                    finally
                    {
                        currentFunction = null;
                    }
                }
            }

            // 4. Evaluate methods
            foreach (var method in context.MethodSymbols)
            {
                if (method.Syntax?.Body is { } body)
                {
                    currentFunction = null;
                    currentMethod = method;
                    try
                    {
                        EvaluateBody(body, context);
                    }
                    finally
                    {
                        currentMethod = null;
                    }
                }
            }

            // 5. Evaluate properties
            foreach (var prop in context.PropertySymbols)
            {
                if (prop.Syntax is not null)
                {
                    foreach (var acc in prop.Syntax.Body.Accessors)
                    {
                        if (acc.Body is { } body)
                            EvaluateBody(body, context);
                    }
                }
            }
        }
        finally
        {
            reportedWarnings = null;
            constContextDepth = 0;
        }
    }

    private static void EvaluateBody(FunctionBody body, ResolutionContext context)
    {
        switch (body)
        {
            case FunctionBlockBody block:
                EvaluateLocals(block.Locals, context);
                break;
            case FunctionLambdaBody lambda:
                EvaluateLocals([lambda.Statement], context);
                break;
        }
    }

    private static void EvaluateLocals(System.Collections.Generic.IReadOnlyList<Local> locals, ResolutionContext context)
    {
        foreach (var local in locals)
        {
            switch (local)
            {
                case LocalExpressionStatement exprStmt:
                    Evaluate(exprStmt.Expression, context);
                    break;
                case LocalVariableDeclarationStatement varStmt:
                    if (varStmt.Declaration.Declarator.Initializer?.Initializer is { } initExpr)
                    {
                        bool isConst = varStmt.Declaration.Modifiers.Any(m => m.MatchingKind == MatchingKeywordKind.Const);
                        if (isConst) constContextDepth++;
                        try { Evaluate(initExpr, context); }
                        finally { if (isConst) constContextDepth--; }
                    }
                    break;
                case LocalReturnStatement retStmt:
                    if (retStmt.Statement.Expression is { } retExpr)
                        Evaluate(retExpr, context);
                    break;
            }
        }
    }

    internal static ConstantValue Evaluate(Expression expr, ResolutionContext context)
    {
        if (context.TryGetConstantValue(expr, out var cached))
            return cached;

        var type = context.GetExpressionType(expr);
        if (type is null || type.Value.IsError)
            return ConstantValue.None;

        var result = EvaluateCore(expr, context);
        if (result.Kind != ConstantValueKind.None)
            context.SetConstantValue(expr, result);

        return result;
    }

    private static ConstantValue EvaluateCore(Expression expr, ResolutionContext context)
    {
        switch (expr)
        {
            case LiteralExpression lit:
                return EvaluateLiteral(lit);

            case NamedExpression named:
                if (named.Identifier.MatchingKind is MatchingKeywordKind.True)
                    return ConstantValue.FromBool(true);
                if (named.Identifier.MatchingKind is MatchingKeywordKind.False)
                    return ConstantValue.FromBool(false);

                if (context.ResolvedTree.TryGetReference(named, out var h))
                {
                    Symbol? sym = null;
                    if (h.Kind == SymbolKind.Variable)
                    {
                        if (currentFunction is not null && (int)h.ID >= 0 && (int)h.ID < currentFunction.LocalVariables.Count)
                            sym = currentFunction.LocalVariables[(int)h.ID];
                        else if (currentMethod is not null && (int)h.ID >= 0 && (int)h.ID < currentMethod.LocalVariables.Count)
                            sym = currentMethod.LocalVariables[(int)h.ID];
                    }
                    else
                    {
                        sym = context.GetSymbol(h);
                    }

                    if (sym is LocalVariableSymbol local && local.Flags.HasFlag(VariableFlags.Const))
                    {
                        if (local.Syntax?.Declarator.Initializer?.Initializer is { } initExpr)
                        {
                            constContextDepth++;
                            try { return Evaluate(initExpr, context); }
                            finally { constContextDepth--; }
                        }
                    }
                    else if (sym is FieldSymbol field && field.Flags.HasFlag(VariableFlags.Const))
                    {
                        if (field.Syntax?.Declarator.Initializer?.Initializer is { } initExpr)
                        {
                            constContextDepth++;
                            try { return Evaluate(initExpr, context); }
                            finally { constContextDepth--; }
                        }
                    }
                    else if (sym is GlobalVariableSymbol global && global.Flags.HasFlag(VariableFlags.Const))
                    {
                        if (global.Syntax?.Declarator.Initializer?.Initializer is { } initExpr)
                        {
                            constContextDepth++;
                            try { return Evaluate(initExpr, context); }
                            finally { constContextDepth--; }
                        }
                    }
                }
                return ConstantValue.None;

            case ParenthesizedExpression paren:
                return Evaluate(paren.Expression, context);

            case UnaryExpression unary:
                return EvaluateUnary(unary, context);

            case BinaryExpression binary:
                return EvaluateBinary(binary, context);

            case IfExpression ifExpr:
                return EvaluateIf(ifExpr, context);

            case WhileExpression whileExpr:
                Evaluate(whileExpr.Condition, context);
                Evaluate(whileExpr.Body, context);
                return ConstantValue.None;

            case BlockExpression blockExpr:
                EvaluateLocals(blockExpr.Locals, context);
                ConstantValue last = ConstantValue.None;
                if (blockExpr.Locals.Count > 0 && blockExpr.Locals[^1] is LocalExpressionStatement lastEs)
                    last = Evaluate(lastEs.Expression, context);
                return last;

            default:
                return ConstantValue.None;
        }
    }

    private static ConstantValue EvaluateLiteral(LiteralExpression lit)
    {
        var token = lit.Literal;
        if (token.MatchingKind is MatchingKeywordKind.True) return ConstantValue.FromBool(true);
        if (token.MatchingKind is MatchingKeywordKind.False) return ConstantValue.FromBool(false);

        var text = token.ValueWithoutSuffix;

        if (token.Kind is TokenKind.Integer or TokenKind.SuffixedInteger)
        {
            if (TryParseBigInteger(text, out var bi))
                return ConstantValue.FromInt(bi);
        }
        else if (token.Kind is TokenKind.Float or TokenKind.SuffixedFloat)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return ConstantValue.FromFloat(d);
        }
        else if (token.Kind is TokenKind.String or TokenKind.SuffixedString)
        {
            return ConstantValue.FromStr(UnescapeString(text));
        }
        else if (token.Kind is TokenKind.Char or TokenKind.SuffixedChar)
        {
            var unquoted = UnescapeString(text);
            if (unquoted.Length > 0)
            {
                uint codePoint = (uint)char.ConvertToUtf32(unquoted, 0);
                return ConstantValue.FromChar(codePoint);
            }
        }

        return ConstantValue.None;
    }

    private static bool TryParseBigInteger(string text, out BigInteger val)
    {
        val = BigInteger.Zero;
        if (string.IsNullOrEmpty(text))
            return false;

        text = text.Replace("_", "");

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = text.Substring(2);
            return BigInteger.TryParse("0" + hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val);
        }

        if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            var bin = text.Substring(2);
            BigInteger b = 0;
            foreach (var ch in bin)
            {
                if (ch != '0' && ch != '1') return false;
                b = (b << 1) | (ch == '1' ? 1 : 0);
            }
            val = b;
            return true;
        }

        return BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out val);
    }

    private static string UnescapeString(string text)
    {
        if (text.Length >= 2 && ((text.StartsWith('"') && text.EndsWith('"')) || (text.StartsWith('\'') && text.EndsWith('\''))))
            text = text.Substring(1, text.Length - 2);

        return text;
    }

    private static ConstantValue EvaluateUnary(UnaryExpression unary, ResolutionContext context)
    {
        var operand = Evaluate(unary.Operand, context);
        if (operand.Kind == ConstantValueKind.None)
            return ConstantValue.None;

        var opKind = unary.OperatorToken.Kind;
        if (opKind == TokenKind.Minus)
        {
            if (operand.Kind == ConstantValueKind.Int)
                return ConstantValue.FromInt(-operand.AsInt);
            if (operand.Kind == ConstantValueKind.Float)
                return ConstantValue.FromFloat(-operand.AsFloat);
        }
        else if (opKind == TokenKind.Plus)
        {
            if (operand.Kind == ConstantValueKind.Int || operand.Kind == ConstantValueKind.Float)
                return operand;
        }
        else if (opKind == TokenKind.ExclamationMark)
        {
            if (operand.Kind == ConstantValueKind.Bool)
                return ConstantValue.FromBool(!operand.AsBool);
        }
        else if (opKind == TokenKind.Tilde)
        {
            if (operand.Kind == ConstantValueKind.Int)
                return ConstantValue.FromInt(~operand.AsInt);
        }

        return ConstantValue.None;
    }

    private static ConstantValue EvaluateBinary(BinaryExpression binary, ResolutionContext context)
    {
        var opKind = binary.OperatorToken.Kind;

        // Logical short-circuiting:
        if (opKind == TokenKind.AmpersandAmpersand)
        {
            var left = Evaluate(binary.LeftExpression, context);
            if (left.IsFalse)
                return ConstantValue.FromBool(false);
            if (left.IsTrue)
            {
                var right = Evaluate(binary.RightExpression, context);
                return right.Kind == ConstantValueKind.Bool ? right : ConstantValue.None;
            }
            return ConstantValue.None;
        }

        if (opKind == TokenKind.VerticalBarVerticalBar)
        {
            var left = Evaluate(binary.LeftExpression, context);
            if (left.IsTrue)
                return ConstantValue.FromBool(true);
            if (left.IsFalse)
            {
                var right = Evaluate(binary.RightExpression, context);
                return right.Kind == ConstantValueKind.Bool ? right : ConstantValue.None;
            }
            return ConstantValue.None;
        }

        var l = Evaluate(binary.LeftExpression, context);
        var r = Evaluate(binary.RightExpression, context);

        if (l.Kind == ConstantValueKind.None || r.Kind == ConstantValueKind.None)
            return ConstantValue.None;

        if (l.Kind == ConstantValueKind.Int && r.Kind == ConstantValueKind.Int)
        {
            var li = l.AsInt;
            var ri = r.AsInt;

            if ((opKind is TokenKind.ForwardSlash or TokenKind.Percentage) && ri == 0)
            {
                if (constContextDepth > 0)
                {
                    context.Diagnostics.ReportConstDivisionByZero(binary.OperatorToken.Span, binary.GetSource());
                }
                else
                {
                    if (reportedWarnings?.Add(binary) != false)
                        context.Diagnostics.ReportDivisionByZeroWarning(binary.OperatorToken.Span, binary.GetSource());
                }
                return ConstantValue.None;
            }

            return opKind switch
            {
                TokenKind.Plus => ConstantValue.FromInt(li + ri),
                TokenKind.Minus => ConstantValue.FromInt(li - ri),
                TokenKind.Asterisk => ConstantValue.FromInt(li * ri),
                TokenKind.ForwardSlash => ConstantValue.FromInt(li / ri),
                TokenKind.Percentage => ConstantValue.FromInt(li % ri),
                TokenKind.Ampersand => ConstantValue.FromInt(li & ri),
                TokenKind.VerticalBar => ConstantValue.FromInt(li | ri),
                TokenKind.Caret => ConstantValue.FromInt(li ^ ri),
                TokenKind.LessThanLessThanSigns => ConstantValue.FromInt(li << (int)ri),
                TokenKind.GreaterThanGreaterThanSigns => ConstantValue.FromInt(li >> (int)ri),
                TokenKind.EqualsEquals => ConstantValue.FromBool(li == ri),
                TokenKind.ExclamationEquals => ConstantValue.FromBool(li != ri),
                TokenKind.LessThanSign => ConstantValue.FromBool(li < ri),
                TokenKind.LessThanEquals => ConstantValue.FromBool(li <= ri),
                TokenKind.GreaterThanSign => ConstantValue.FromBool(li > ri),
                TokenKind.GreaterThanEquals => ConstantValue.FromBool(li >= ri),
                _ => ConstantValue.None
            };
        }

        if (l.Kind == ConstantValueKind.Float && r.Kind == ConstantValueKind.Float)
        {
            var lf = l.AsFloat;
            var rf = r.AsFloat;

            if ((opKind is TokenKind.ForwardSlash or TokenKind.Percentage) && rf == 0.0)
            {
                if (constContextDepth > 0)
                {
                    context.Diagnostics.ReportConstDivisionByZero(binary.OperatorToken.Span, binary.GetSource());
                }
                else
                {
                    if (reportedWarnings?.Add(binary) != false)
                        context.Diagnostics.ReportDivisionByZeroWarning(binary.OperatorToken.Span, binary.GetSource());
                }
                return ConstantValue.None;
            }

            return opKind switch
            {
                TokenKind.Plus => ConstantValue.FromFloat(lf + rf),
                TokenKind.Minus => ConstantValue.FromFloat(lf - rf),
                TokenKind.Asterisk => ConstantValue.FromFloat(lf * rf),
                TokenKind.ForwardSlash => ConstantValue.FromFloat(lf / rf),
                TokenKind.Percentage => ConstantValue.FromFloat(lf % rf),
                TokenKind.EqualsEquals => ConstantValue.FromBool(lf == rf),
                TokenKind.ExclamationEquals => ConstantValue.FromBool(lf != rf),
                TokenKind.LessThanSign => ConstantValue.FromBool(lf < rf),
                TokenKind.LessThanEquals => ConstantValue.FromBool(lf <= rf),
                TokenKind.GreaterThanSign => ConstantValue.FromBool(lf > rf),
                TokenKind.GreaterThanEquals => ConstantValue.FromBool(lf >= rf),
                _ => ConstantValue.None
            };
        }

        if (l.Kind == ConstantValueKind.Bool && r.Kind == ConstantValueKind.Bool)
        {
            var lb = l.AsBool;
            var rb = r.AsBool;

            return opKind switch
            {
                TokenKind.EqualsEquals => ConstantValue.FromBool(lb == rb),
                TokenKind.ExclamationEquals => ConstantValue.FromBool(lb != rb),
                TokenKind.Ampersand => ConstantValue.FromBool(lb & rb),
                TokenKind.VerticalBar => ConstantValue.FromBool(lb | rb),
                TokenKind.Caret => ConstantValue.FromBool(lb ^ rb),
                _ => ConstantValue.None
            };
        }

        if (l.Kind == ConstantValueKind.Char && r.Kind == ConstantValueKind.Char)
        {
            var lc = l.AsChar;
            var rc = r.AsChar;

            return opKind switch
            {
                TokenKind.EqualsEquals => ConstantValue.FromBool(lc == rc),
                TokenKind.ExclamationEquals => ConstantValue.FromBool(lc != rc),
                TokenKind.LessThanSign => ConstantValue.FromBool(lc < rc),
                TokenKind.LessThanEquals => ConstantValue.FromBool(lc <= rc),
                TokenKind.GreaterThanSign => ConstantValue.FromBool(lc > rc),
                TokenKind.GreaterThanEquals => ConstantValue.FromBool(lc >= rc),
                _ => ConstantValue.None
            };
        }

        if (l.Kind == ConstantValueKind.Str && r.Kind == ConstantValueKind.Str)
        {
            var ls = l.AsStr;
            var rs = r.AsStr;

            return opKind switch
            {
                TokenKind.Plus => ConstantValue.FromStr(ls + rs),
                TokenKind.EqualsEquals => ConstantValue.FromBool(ls == rs),
                TokenKind.ExclamationEquals => ConstantValue.FromBool(ls != rs),
                _ => ConstantValue.None
            };
        }

        return ConstantValue.None;
    }

    private static ConstantValue EvaluateIf(IfExpression ifExpr, ResolutionContext context)
    {
        var cond = Evaluate(ifExpr.Condition, context);
        var thenVal = Evaluate(ifExpr.ThenExpression, context);
        var elseVal = ifExpr.ElseExpression is { } elseExpr ? Evaluate(elseExpr.Expression, context) : ConstantValue.None;

        if (cond.IsTrue)
            return thenVal;
        if (cond.IsFalse)
            return elseVal;

        return ConstantValue.None;
    }
}
