namespace Maho.Symbols;

/// <summary> Work-In-Progress. This class will represent types in the language. </summary>
/// <param name="type"> The identifier of the type. </param>
internal abstract class TypeSymbol(string type)
{
    /// <summary> The identifier of the type. </summary>
    public string Type { get; set; } = type;
}
