using System.Collections.Generic;

namespace Maho.Resolution.Layout;

/// <summary>
/// Platform-independent contiguity analyzer for types with deduplicated base inheritance.
/// <para>
/// Determines whether a base sub-object's field sequence occupies an unbroken, consecutive sub-slice
/// within the derived type's C3-linearized field sequence (enabling thin reference coercions),
/// or whether foreign fields are interleaved (requiring dynamic 'dyn' existential references).
/// </para>
/// </summary>
internal static class TypeContiguityAnalyzer
{
    private static readonly Dictionary<SymbolHandle, List<SymbolHandle>> fieldSequenceCache = [];

    public static void ClearCache() => fieldSequenceCache.Clear();

    /// <summary>
    /// Computes the complete topological field sequence for the specified type in C3 linearization DAG order.
    /// Deduplicated bases are placed first in dependency order, followed by the type's own declared fields.
    /// </summary>
    public static IReadOnlyList<SymbolHandle> GetFieldSequence(SymbolHandle typeHandle, ResolutionContext context)
    {
        typeHandle = context.UnwrapAlias(typeHandle);
        if (fieldSequenceCache.TryGetValue(typeHandle, out var cached))
            return cached;

        var placedBases = new HashSet<SymbolHandle>();
        var sequence = new List<SymbolHandle>();

        void PlaceBaseRecursive(SymbolHandle bHandle)
        {
            bHandle = context.UnwrapAlias(bHandle);
            if (bHandle == typeHandle)
                return;

            var sym = context.GetSymbol(bHandle);
            IReadOnlyList<TypeRef>? directBases = sym switch
            {
                TypeSymbol ts => ts.BaseTypes,
                NestedTypeSymbol nts => nts.BaseTypes,
                _ => null
            };

            if (directBases is not null)
            {
                foreach (var db in directBases)
                {
                    if (db.Handle is { } dbh)
                        PlaceBaseRecursive(dbh);
                }
            }

            if (placedBases.Add(bHandle))
            {
                var ownFields = GetOwnInstanceFields(bHandle, context);
                sequence.AddRange(ownFields);
            }
        }

        var rootSym = context.GetSymbol(typeHandle);
        IReadOnlyList<TypeRef>? rootDirectBases = rootSym switch
        {
            TypeSymbol ts => ts.BaseTypes,
            NestedTypeSymbol nts => nts.BaseTypes,
            _ => null
        };

        if (rootDirectBases is not null)
        {
            foreach (var b in rootDirectBases)
            {
                if (b.Handle is { } bh)
                    PlaceBaseRecursive(bh);
            }
        }

        var ownRootFields = GetOwnInstanceFields(typeHandle, context);
        sequence.AddRange(ownRootFields);

        fieldSequenceCache[typeHandle] = sequence;
        return sequence;
    }

    /// <summary>
    /// Checks whether the fields of <paramref name="baseHandle"/> appear as an unbroken,
    /// consecutive sub-slice in the linearized field sequence of <paramref name="derivedHandle"/>.
    /// </summary>
    public static bool IsBaseContiguous(SymbolHandle derivedHandle, SymbolHandle baseHandle, ResolutionContext context)
    {
        derivedHandle = context.UnwrapAlias(derivedHandle);
        baseHandle = context.UnwrapAlias(baseHandle);

        if (derivedHandle == baseHandle)
            return true;

        var baseFields = GetFieldSequence(baseHandle, context);
        if (baseFields.Count == 0)
        {
            // Empty base has no fields that could be split by foreign interleaving
            return true;
        }

        var derivedFields = GetFieldSequence(derivedHandle, context);
        if (derivedFields.Count < baseFields.Count)
            return false;

        // Find the index of the first base field in the derived field sequence
        int startIndex = -1;
        for (int i = 0; i <= derivedFields.Count - baseFields.Count; i++)
        {
            if (derivedFields[i] == baseFields[0])
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex == -1)
            return false;

        // Verify that every subsequent base field appears consecutively without interleaving
        for (int j = 0; j < baseFields.Count; j++)
        {
            if (derivedFields[startIndex + j] != baseFields[j])
                return false;
        }

        return true;
    }

    public static BaseContiguity GetContiguity(SymbolHandle derivedHandle, SymbolHandle baseHandle, ResolutionContext context) =>
        IsBaseContiguous(derivedHandle, baseHandle, context) ? BaseContiguity.Contiguous : BaseContiguity.Split;

    private static List<SymbolHandle> GetOwnInstanceFields(SymbolHandle typeHandle, ResolutionContext context)
    {
        var result = new List<SymbolHandle>();
        var sym = context.GetSymbol(typeHandle);
        if (sym is null)
            return result;

        var scope = sym.EnclosingScope.GetChildScope(ResolutionContext.GetHandle(sym)) ?? sym.EnclosingScope;
        foreach (var s in scope.Symbols.Values)
        {
            if (s is FieldSymbol fs && !fs.Flags.HasFlag(VariableFlags.Static))
            {
                result.Add(ResolutionContext.GetHandle(fs));
            }
        }

        return result;
    }
}

