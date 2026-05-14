using System.ComponentModel.DataAnnotations;



namespace JOIN.Domain.Enums;



/// <summary>
/// Defines the categories of communication for a Person.
/// Applies the Open/Closed Principle by consolidating Emails and Phones into a single structure.
/// </summary>
public enum ContactType
{


    [Display(Name = "Correo Principal")]
    PrimaryEmail = 1,

    [Display(Name = "Correo Alternativo")]
    AlternativeEmail = 2,

    [Display(Name = "Teléfono Móvil")]
    MobilePhone = 3,

    [Display(Name = "Teléfono Fijo")]
    Landline = 4,

    [Display(Name = "WhatsApp")]
    WhatsApp = 5,

    [Display(Name = "Otro")]
    Other = 99


}
