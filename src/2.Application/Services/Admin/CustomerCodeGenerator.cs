using System.Globalization;
using JOIN.Application.Interface.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;

namespace JOIN.Application.Services.Admin;

/// <summary>
/// Generates sequential customer codes in the format C000000001 (10 characters).
/// </summary>
public sealed class CustomerCodeGenerator(IUnitOfWork unitOfWork) : ICustomerCodeGenerator
{
    private const char CodePrefix = 'C';
    private const int MaxCodeLength = 10;
    private const int NumericSuffixLength = 9;

    /// <inheritdoc />
    public async Task<string> GenerateNextAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var repository = unitOfWork.GetRepository<Customer>();
        var customers = await repository.GetAllAsync();

        var maxSequence = customers
            .Where(c => c.CompanyId == companyId)
            .Select(c => c.CustomerCode)
            .Select(TryParseSequence)
            .Where(sequence => sequence.HasValue)
            .Select(sequence => sequence!.Value)
            .DefaultIfEmpty(0)
            .Max();

        var nextSequence = maxSequence + 1;
        var code = $"{CodePrefix}{nextSequence.ToString($"D{NumericSuffixLength}", CultureInfo.InvariantCulture)}";

        if (code.Length > MaxCodeLength)
        {
            throw new InvalidOperationException("Customer code sequence exceeded the maximum length of 10 characters.");
        }

        return code;
    }

    private static long? TryParseSequence(string? customerCode)
    {
        if (string.IsNullOrWhiteSpace(customerCode)
            || customerCode.Length < 2
            || customerCode[0] != CodePrefix)
        {
            return null;
        }

        return long.TryParse(
            customerCode[1..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var sequence)
            ? sequence
            : null;
    }
}
