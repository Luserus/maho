namespace Maho.Symbols;

/// <summary> Work-In-Progress. This class will represent types in the language. </summary>
internal abstract class TypeSymbol
{
    /// <summary> The identifier of the type. </summary>
    public string Type { get; set; }

    /// <summary> Initializes the TypeSymbol class. </summary>
    /// <param name="type"> The identifier of the type. </param>
    public TypeSymbol(string type)
    {
        Type = type;
    }
}
