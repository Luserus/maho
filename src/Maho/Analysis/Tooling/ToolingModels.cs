using System.Collections.Generic;

namespace Maho.Tooling;

/// <summary>
/// Serializable DTO for Go-to-Definition results.
/// </summary>
public sealed record DefinitionLocation(
    string FilePath,
    TextSpanInfo Span,
    TextSpanInfo SelectionSpan,
    string SymbolName,
    string SymbolKind,
    string? ContainerName = null);

/// <summary>
/// Serializable DTO for Find-All-References results.
/// </summary>
public sealed record ReferenceLocation(
    string FilePath,
    TextSpanInfo Span,
    bool IsDeclaration,
    string? ContextLine = null);

/// <summary>
/// Serializable DTO for Hover tooltip requests.
/// </summary>
public sealed record HoverInfo(
    TextSpanInfo Span,
    string DisplayString,
    string? QualifiedType = null,
    string? Documentation = null);

/// <summary>
/// Serializable DTO for Document Symbols / Outline views.
/// </summary>
public sealed record DocumentSymbolInfo(
    string Name,
    string Kind,
    TextSpanInfo Span,
    TextSpanInfo SelectionSpan,
    string? Detail = null,
    IReadOnlyList<DocumentSymbolInfo>? Children = null);

/// <summary>
/// Serializable DTO for Autocomplete / Completion lists.
/// </summary>
public sealed record CompletionItemInfo(
    string Label,
    string Kind,
    string? Detail = null,
    string? Documentation = null,
    string? InsertText = null);

/// <summary>
/// Serializable DTO for Signature Help / Parameter Info.
/// </summary>
public sealed record SignatureHelpInfo(
    IReadOnlyList<SignatureItemInfo> Signatures,
    int ActiveSignature,
    int ActiveParameter);

/// <summary>
/// Serializable DTO describing a single callable overload signature.
/// </summary>
public sealed record SignatureItemInfo(
    string Label,
    string? Documentation,
    IReadOnlyList<ParameterItemInfo> Parameters);

/// <summary>
/// Serializable DTO describing a single parameter in a signature.
/// </summary>
public sealed record ParameterItemInfo(
    string Label,
    string? Documentation);

/// <summary>
/// Serializable DTO for Semantic Syntax Highlighting tokens.
/// </summary>
public sealed record SemanticTokenInfo(
    int Line,
    int Column,
    int Length,
    string TokenType,
    IReadOnlyList<string>? Modifiers = null);
