namespace JOIN.Application.Interface.Admin;



/// <summary>
/// Generates unique customer codes per tenant.
/// </summary>
public interface ICustomerCodeGenerator
{
    /// <summary>
    /// Generates the next available customer code for the specified company (max 10 characters).
    /// </summary>
    Task<string> GenerateNextAsync(Guid companyId, CancellationToken cancellationToken = default);
}
