
using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Catalog for identification document types (e.g., DNI, Passport, TAX ID).
/// Essential for legal compliance and entity verification.
/// </summary>
public class IdentificationType : BaseAuditableEntity
{

    /// <summary> Short name or abbreviation (e.g., "DNI", "RUC"). </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Full description of the document type. </summary>
    public string? Description { get; set; }

    /// <summary> Regex pattern to validate the identification number if necessary. </summary>
    public string? ValidationPattern { get; set; }
    
}