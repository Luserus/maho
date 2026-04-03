using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maho.Text;

namespace Maho.Syntax;

internal sealed partial class Parser
{
    public override string ToString()
    {
        Dictionary<SyntaxNode, TextSpan?> spanCache = [];
        return DebugJson.Serialize(new DebugParserInfo("parser", Root is null ? null : CreateNodeView(Root, spanCache)));
    }

    private DebugParserNodeInfo CreateNodeView(SyntaxNode node, Dictionary<SyntaxNode, TextSpan?> spanCache)
    {
        TextSpan? span = GetSpan(node, spanCache);
        List<(string Name, SyntaxNode Node)> children = GetChildren(node);
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

    private TextSpan? GetSpan(SyntaxNode node, Dictionary<SyntaxNode, TextSpan?> spanCache)
    {
        if (spanCache.TryGetValue(node, out TextSpan? cachedSpan))
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
                TextSpan? childSpan = GetSpan(child, spanCache);

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

    private static List<(string Name, SyntaxNode Node)> GetChildren(SyntaxNode node)
    {
        List<(string Name, SyntaxNode Node)> children = [];

        foreach (PropertyInfo property in node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).OrderBy(static property => property.MetadataToken))
        {
            object? value = property.GetValue(node);

            if (value is null or string)
                continue;

            if (value is SyntaxNode child)
            {
                children.Add((property.Name, child));
                continue;
            }

            if (value is IEnumerable sequence)
            {
                int index = 0;

                foreach (object? item in sequence)
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
}