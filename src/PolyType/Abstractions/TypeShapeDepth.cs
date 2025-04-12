namespace PolyType.Abstractions;

/// <summary>
/// Describes the requirements for preparing an associated type.
/// </summary>
[Flags]
public enum TypeShapeDepth
{
    /// <summary>No shape is required.</summary>
    None = 0x0,

    /// <summary>
    /// A constructor should be included in the shape, if one is declared.
    /// </summary>
    Constructor = 0x1,

    /// <summary>
    /// The shape should be fully generated.
    /// </summary>
    All = -1,
}
