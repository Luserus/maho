using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Maho.Syntax;

/// <summary> View over a syntax list that interleaves nodes and separator tokens. </summary>
internal readonly struct SeparatedSyntaxList<T> : IEnumerable<T> where T : SyntaxNode
{
    /// <summary> Underlying node/separator storage in source order. </summary>
    public IReadOnlyList<SyntaxNode> NodesAndSeparators { get; }

    /// <summary> Creates one separated syntax list view. </summary>
    public SeparatedSyntaxList(IReadOnlyList<SyntaxNode> nodesAndSeparators) => NodesAndSeparators = nodesAndSeparators;

    /// <summary> Number of typed nodes in the list. </summary>
    public int Count => NodesAndSeparators.Count(n => n is T);

    /// <summary> Returns the typed node at the requested logical index. </summary>
    public T this[int index] => (T)NodesAndSeparators[index * 2];

    /// <summary> Returns the separator token after the requested node, or <see langword="null"/> for the last entry. </summary>
    public Token? GetSeparator(int index)
    {
        int separatorIndex = index * 2 + 1;

        if (separatorIndex >= NodesAndSeparators.Count)
            return null;

        return (Token)NodesAndSeparators[separatorIndex];
    }

    /// <summary> Enumerates the typed nodes in order. </summary>
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
