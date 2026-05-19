using System.ComponentModel.DataAnnotations;



namespace JOIN.Domain.Enums;



/// <summary>
/// Defines the categories of communication for a Person.
/// Defines the lifecycle stages of a Person within the CRM
/// </summary>
public enum PersonLifecycleStage
{

    
    /// <summary> Initial contact, evaluating if they are a fit. </summary>
    [Display(Name = "En contacto")]
    Lead = 1,

    /// <summary> Currently in a sales process or negotiation. </summary>
    [Display(Name = "En proceso")]
    Prospect = 2,

    /// <summary> Has passed evaluations and is an active, paying customer. </summary>
    [Display(Name = "cliente")]
    Customer = 3,

    /// <summary> Was a customer, but the relationship ended (Churn). </summary>
    [Display(Name = "ClienteInactivo")]
    FormerCustomer = 4

    
}