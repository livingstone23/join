


namespace JOIN.Domain.Enums;



/// <summary>
/// Defines the nature of the customer: Natural person (Física) or Legal entity (Jurídica).
/// This helps to determine which validation rules and tax obligations apply.
/// </summary>
public enum PersonType
{

    /// <summary> Individual person (Persona Física). </summary>
    Physical = 1,
    
    /// <summary> Business or Organization (Persona Jurídica). </summary>
    Legal = 2

}