using System.Collections.Generic;

namespace Maho.Syntax;

/// <summary> Pattern matching an enum variant (unit <c>A</c>, tuple <c>B(x)</c>, struct <c>C { x }</c>). </summary>
internal sealed class EnumVariantPattern : PatternSyntax
{
    public TypeSyntax? TypeQualifier { get; }
    public NamedSyntax Name { get; }
    public EnumVariantKind Kind { get; }
    public Token? OpenParen { get; }
    public SeparatedSyntaxList<PatternSyntax>? SubPatterns { get; }
    public Token? CloseParen { get; }
    public Token? OpenBrace { get; }
    public IReadOnlyList<FieldPatternSyntax>? FieldPatterns { get; }
    public Token? CloseBrace { get; }

    /// <summary> Creates a unit variant pattern. </summary>
    public static EnumVariantPattern Unit(TypeSyntax? typeQualifier, NamedSyntax name) =>
        new(typeQualifier, name, EnumVariantKind.Unit, null, null, null, null, null, null);

    /// <summary> Creates a tuple variant pattern. </summary>
    public static EnumVariantPattern Tuple(TypeSyntax? typeQualifier, NamedSyntax name, Token openParen, SeparatedSyntaxList<PatternSyntax> subPatterns, Token closeParen) =>
        new(typeQualifier, name, EnumVariantKind.Tuple, openParen, subPatterns, closeParen, null, null, null);

    /// <summary> Creates a struct variant pattern. </summary>
    public static EnumVariantPattern Struct(TypeSyntax? typeQualifier, NamedSyntax name, Token openBrace, IReadOnlyList<FieldPatternSyntax> fieldPatterns, Token closeBrace) =>
        new(typeQualifier, name, EnumVariantKind.Struct, null, null, null, openBrace, fieldPatterns, closeBrace);

    private EnumVariantPattern(
        TypeSyntax? typeQualifier,
        NamedSyntax name,
        EnumVariantKind kind,
        Token? openParen,
        SeparatedSyntaxList<PatternSyntax>? subPatterns,
        Token? closeParen,
        Token? openBrace,
        IReadOnlyList<FieldPatternSyntax>? fieldPatterns,
        Token? closeBrace)
    {
        TypeQualifier = typeQualifier;
        Name = name;
        Kind = kind;
        OpenParen = openParen;
        SubPatterns = subPatterns;
        CloseParen = closeParen;
        OpenBrace = openBrace;
        FieldPatterns = fieldPatterns;
        CloseBrace = closeBrace;
    }
}

