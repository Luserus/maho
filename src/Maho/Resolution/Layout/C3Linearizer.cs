using System.Collections.Generic;

namespace Maho.Resolution.Layout;

/// <summary>
/// Computes C3 superclass linearization for multiple-inheritance hierarchies,
/// guaranteeing monotonicity and strictly deduplicating diamond bases in DAG order.
/// </summary>
internal static class C3Linearizer
{
    /// <summary>
    /// Computes the C3 linearization list for the given type handle.
    /// The resulting list starts with the type itself, followed by all direct and transitive bases in C3 order.
    /// Returns true if successful, or false if a cycle or linearization conflict was detected.
    /// </summary>
    public static bool TryLinearize(
        SymbolHandle typeHandle,
        ResolutionContext context,
        out List<SymbolHandle> linearization,
        HashSet<SymbolHandle>? activeCycleDetect = null)
    {
        typeHandle = context.UnwrapAlias(typeHandle);
        linearization = [typeHandle];

        activeCycleDetect ??= [];
        if (!activeCycleDetect.Add(typeHandle))
        {
            // Cycle detected
            return false;
        }

        var sym = context.GetSymbol(typeHandle);
        IReadOnlyList<TypeRef>? directBases = sym switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => null
        };

        if (directBases is null || directBases.Count == 0)
        {
            activeCycleDetect.Remove(typeHandle);
            return true;
        }

        var directHandles = new List<SymbolHandle>();
        var baseLinearizations = new List<List<SymbolHandle>>();

        foreach (var b in directBases)
        {
            if (b.Handle is { } bh)
            {
                var unwrappedBh = context.UnwrapAlias(bh);
                if (!TryLinearize(unwrappedBh, context, out var bLin, activeCycleDetect))
                {
                    activeCycleDetect.Remove(typeHandle);
                    return false;
                }
                baseLinearizations.Add([.. bLin]);
                directHandles.Add(unwrappedBh);
            }
        }

        activeCycleDetect.Remove(typeHandle);

        // Merge: L(C) = [C] + merge(L(B1), L(B2), ..., [B1, B2, ...])
        var inputLists = new List<List<SymbolHandle>>(baseLinearizations)
        {
            new List<SymbolHandle>(directHandles)
        };

        var merged = Merge(inputLists);
        if (merged is null)
            return false;

        linearization.AddRange(merged);
        return true;
    }

    /// <summary>
    /// Performs the C3 merge step:
    /// Repeatedly select the first head of a list that is not in the tail of any other list,
    /// append to output, and remove from all lists.
    /// </summary>
    private static List<SymbolHandle>? Merge(List<List<SymbolHandle>> lists)
    {
        var result = new List<SymbolHandle>();

        while (true)
        {
            // Remove empty lists
            lists.RemoveAll(l => l.Count == 0);
            if (lists.Count == 0)
                break;

            SymbolHandle? nextHead = null;

            foreach (var list in lists)
            {
                var candidate = list[0];
                bool inAnyTail = false;

                foreach (var other in lists)
                {
                    // Tail is elements from index 1 onward
                    for (int i = 1; i < other.Count; i++)
                    {
                        if (other[i] == candidate)
                        {
                            inAnyTail = true;
                            break;
                        }
                    }
                    if (inAnyTail)
                        break;
                }

                if (!inAnyTail)
                {
                    nextHead = candidate;
                    break;
                }
            }

            if (nextHead is null)
            {
                // Inconsistent hierarchy / linearization conflict
                return null;
            }

            result.Add(nextHead.Value);

            foreach (var list in lists)
            {
                if (list.Count > 0 && list[0] == nextHead.Value)
                    list.RemoveAt(0);
            }
        }

        return result;
    }
}
