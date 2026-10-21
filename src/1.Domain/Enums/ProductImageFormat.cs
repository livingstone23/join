namespace JOIN.Domain.Enums;

/// <summary>
/// SPEC 70 — flags of allowed image formats for product images. Separate from <see cref="DocumentType"/>
/// because <c>Image</c> in <see cref="DocumentType"/> does not include <c>.webp</c>, while the product catalog
/// needs to validate by extension AND by signature (rule 7).
/// </summary>
[Flags]
public enum ProductImageFormat
{
    None = 0,

    /// <summary>.jpg, .jpeg</summary>
    Jpeg = 1 << 0,

    /// <summary>.png</summary>
    Png = 1 << 1,

    /// <summary>.webp</summary>
    Webp = 1 << 2
}