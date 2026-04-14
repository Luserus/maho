using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maho.Text;

namespace Maho.Syntax;

/// <summary> Shared span/source helpers for syntax nodes that do not store coordinates directly. </summary>
internal static class SyntaxSpan
{
    /// <summary> Computes a best-effort span for one syntax node by spanning its token-bearing children. </summary>
    public static TextSpan? GetSpan(SyntaxNode node)
    {
        if (node is Token token)
            return token.Span;

        TextSpan? first = null;
        TextSpan? last = null;

        foreach ((_, SyntaxNode child) in GetChildren(node))
        {
            TextSpan? childSpan = GetSpan(child);

            if (childSpan is not TextSpan concreteSpan)
                continue;

            first ??= concreteSpan;
            last = concreteSpan;
        }

        return first is TextSpan firstSpan && last is TextSpan lastSpan
            ? TextSpan.FromBounds(firstSpan.Start, lastSpan.End)
            : null;
    }

    /// <summary> Resolves the backing source for one syntax node from the first token reachable under it. </summary>
    public static SourceText? GetSource(SyntaxNode node) => GetFirstToken(node)?.Source;

    /// <summary> Finds the first token reachable under this syntax node in declaration order. </summary>
    public static Token? GetFirstToken(SyntaxNode node)
    {
        if (node is Token token)
            return token;

        foreach ((_, SyntaxNode child) in GetChildren(node))
        {
            Token? firstToken = GetFirstToken(child);

            if (firstToken is not null)
                return firstToken;
        }

        return null;
    }

    /// <summary> Reflects over public properties to discover syntax children in source/declaration order. </summary>
    internal static List<(string Name, SyntaxNode Node)> GetChildren(SyntaxNode node)
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
