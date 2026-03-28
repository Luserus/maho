using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    private const string Reset = "\u001b[0m";
    private const string Dim = "\u001b[2m";
    private const string BrightWhite = "\u001b[97m";
    private const string Cyan = "\u001b[36m";
    private const string Green = "\u001b[32m";
    private const string Yellow = "\u001b[33m";
    private const string Magenta = "\u001b[35m";
    private const string Blue = "\u001b[34m";

    public override string ToString()
    {
        if (Root is null)
            return "Syntax Tree\n\n<unparsed>";

        Dictionary<SyntaxNode, TextSpan?> spanCache = [];
        StringBuilder sb = new();

        sb.AppendLine("Syntax Tree");
        sb.AppendLine();

        AppendNode(sb, Root, string.Empty, isLast: true, propertyName: null, spanCache);

        return sb.ToString();
    }

    public string ToJson()
    {
        Dictionary<SyntaxNode, TextSpan?> spanCache = [];
        return DebugJson.Serialize(new DebugParserInfo("parser", Root is null ? null : CreateNodeView(Root, spanCache)));
    }

    private void AppendNode(StringBuilder sb, SyntaxNode node, string indent, bool isLast, string? propertyName, Dictionary<SyntaxNode, TextSpan?> spanCache)
    {
        sb.Append(Colorize(indent, Dim));
        sb.Append(Colorize(isLast ? "└── " : "├── ", Dim));

        if (!string.IsNullOrEmpty(propertyName))
        {
            sb.Append(Colorize(propertyName, Cyan));
            sb.Append(Colorize(" -> ", Dim));
        }

        sb.AppendLine(FormatNode(node, spanCache));

        string childIndent = indent + (isLast ? "    " : "│   ");
        List<(string Name, SyntaxNode Node)> children = GetChildren(node);

        for (int i = 0; i < children.Count; i++)
        {
            var (name, child) = children[i];
            AppendNode(sb, child, childIndent, i == children.Count - 1, name, spanCache);
        }
    }

    private string FormatNode(SyntaxNode node, Dictionary<SyntaxNode, TextSpan?> spanCache)
    {
        TextSpan? span = GetSpan(node, spanCache);
        string spanText = span is TextSpan value ? $" {Colorize(FormatSpan(value), Dim)}" : string.Empty;

        if (node is Token token)
        {
            string matchingKind = token.MatchingKind is MatchingKeywordKind.None
                ? string.Empty
                : Colorize($"/{token.MatchingKind}", Magenta);

            return $"{Colorize("Token", Blue)} {Colorize(token.Kind.ToString(), Yellow)}{matchingKind} {Colorize(FormatTokenValue(token), BrightWhite)}{spanText}";
        }

        return $"{Colorize(node.GetType().Name, Green)}{spanText}";
    }

    private TextSpan? GetSpan(SyntaxNode node, Dictionary<SyntaxNode, TextSpan?> spanCache)
    {
        if (spanCache.TryGetValue(node, out var cachedSpan))
            return cachedSpan;

        TextSpan? span;

        if (node is Token token)
        {
            span = token.Span;
        }
        else
        {
            TextSpan? first = null;
            TextSpan? last = null;

            foreach (var (_, child) in GetChildren(node))
            {
                var childSpan = GetSpan(child, spanCache);

                if (childSpan is not TextSpan concreteSpan)
                    continue;

                first ??= concreteSpan;
                last = concreteSpan;
            }

            span = first is TextSpan firstSpan && last is TextSpan lastSpan
                ? TextSpan.FromBounds(firstSpan.Start, lastSpan.End)
                : null;
        }

        spanCache[node] = span;
        return span;
    }

    private DebugParserNodeInfo CreateNodeView(SyntaxNode node, Dictionary<SyntaxNode, TextSpan?> spanCache)
    {
        var span = GetSpan(node, spanCache);
        var children = GetChildren(node);
        DebugParserChildInfo[] childItems = new DebugParserChildInfo[children.Count];

        for (int i = 0; i < children.Count; i++)
        {
            var (name, child) = children[i];
            childItems[i] = new DebugParserChildInfo(name, CreateNodeView(child, spanCache));
        }

        if (node is Token token)
        {
            return new DebugParserNodeInfo(
                node.GetType().Name,
                DebugJson.CreateSpan(text, token.Span),
                token.Kind.ToString(),
                token.Value,
                DebugJson.GetDisplayText(token),
                DebugJson.GetMatchingKind(token.MatchingKind),
                DebugJson.CreateTrivia(text, token.LeadingTrivia),
                DebugJson.CreateTrivia(text, token.TrailingTrivia),
                childItems);
        }

        return new DebugParserNodeInfo(
            node.GetType().Name,
            span is TextSpan concreteSpan ? DebugJson.CreateSpan(text, concreteSpan) : null,
            null,
            null,
            null,
            null,
            null,
            null,
            childItems);
    }

    private static List<(string Name, SyntaxNode Node)> GetChildren(SyntaxNode node)
    {
        List<(string Name, SyntaxNode Node)> children = [];

        foreach (var property in node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).OrderBy(property => property.MetadataToken))
        {
            var value = property.GetValue(node);

            if (value is null || value is string)
                continue;

            if (value is SyntaxNode child)
            {
                children.Add((property.Name, child));
                continue;
            }

            if (value is IEnumerable sequence)
            {
                int index = 0;

                foreach (var item in sequence)
                {
                    if (item is SyntaxNode sequenceChild)
                    {
                        children.Add(($"{property.Name}[{index}]", sequenceChild));
                        index++;
                    }
                }
            }
        }

        return children;
    }

    private string FormatSpan(TextSpan span)
    {
        int startLine = span.GetStartLine(text) + 1;
        int startColumn = span.GetStartColumn(text) + 1;
        int endLine = span.GetEndLine(text) + 1;
        int endColumn = span.GetEndColumn(text) + 1;

        return $"[{span.Start}..{span.End}), len: {span.Length}, ({startLine}, {startColumn})..({endLine}, {endColumn})";
    }

    private static string FormatTokenValue(Token token)
    {
        if (token.Kind is TokenKind.EndToken)
            return "\"<eof>\"";

        if (token.Kind is TokenKind.MissingToken)
            return "\"<missing>\"";

        string value = Escape(token.Value);

        return string.IsNullOrEmpty(value)
            ? "\"\""
            : $"\"{value}\"";
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace("\\", "\\\\")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t")
            .Replace("\"", "\\\"");
    }

    private static string Colorize(string value, string color)
    {
        if (!ShouldUseColor())
            return value;

        return $"{color}{value}{Reset}";
    }

    private static bool ShouldUseColor()
    {
        if (Console.IsOutputRedirected)
            return false;

        if (Environment.GetEnvironmentVariable("NO_COLOR") is not null)
            return false;

        string? term = Environment.GetEnvironmentVariable("TERM");
        return !string.IsNullOrEmpty(term) && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);
    }
}