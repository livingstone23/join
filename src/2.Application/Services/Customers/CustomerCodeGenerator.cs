using System.Globalization;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Customers;

namespace JOIN.Application.Services.Customers;

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
        var customers = await repository.GetAllIncludingDeletedAsync(c => c.CompanyId == companyId);

        var maxSequence = customers
            .Select(c => TryParseSequence(c.CustomerCode))
            .Where(sequence => sequence.HasValue)
            .Select(sequence => sequence!.Value)
            .DefaultIfEmpty(0)
            .Max();

        return Format(maxSequence + 1);
    }

    /// <summary>
    /// Formats a sequence number as a customer code (<c>C</c> + 9 digits). Also used by the seeder (SPEC 54).
    /// </summary>
    public static string Format(long sequence)
    {
        var code = $"{CodePrefix}{sequence.ToString($"D{NumericSuffixLength}", CultureInfo.InvariantCulture)}";

        if (code.Length > MaxCodeLength)
        {
            throw new InvalidOperationException("Customer code sequence exceeded the maximum length of 10 characters.");
        }

        return code;
    }

    /// <summary>
    /// Parses the numeric part of a customer code, or <c>null</c> when it does not follow the format.
    /// </summary>
    public static long? TryParseSequence(string? customerCode)
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
