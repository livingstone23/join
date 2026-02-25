namespace JOIN.Domain.Enums;



/// <summary>
/// Defines the categories of communication for a Customer.
/// Applies the Open/Closed Principle by consolidating Emails and Phones into a single structure.
/// </summary>
public enum ContactType
{
    PrimaryEmail = 1,
    AlternativeEmail = 2,
    MobilePhone = 3,
    Landline = 4,
    WhatsApp = 5,
    Other = 99
}
