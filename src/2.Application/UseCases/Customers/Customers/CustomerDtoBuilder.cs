using JOIN.Application.DTO.Customers;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Customers;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;

namespace JOIN.Application.UseCases.Customers.Customers;

/// <summary>
/// Builds <see cref="CustomerResponseDto"/> from entities inside a command's transaction, where a Dapper read on
/// another connection would not see the uncommitted rows (SPEC 55).
/// </summary>
internal static class CustomerDtoBuilder
{
    /// <param name="unitOfWork">The command's unit of work.</param>
    /// <param name="customer">The customer.</param>
    /// <param name="loggedNow">The event this command just logged (source <c>Manual</c>), or <c>null</c> to read the stored history.</param>
    internal static async Task<CustomerResponseDto> BuildAsync(IUnitOfWork unitOfWork, Customer customer, CustomerStatusAction? loggedNow)
    {
        var person = (await unitOfWork.GetRepository<Person>()
            .GetAllIncludingDeletedAsync(p => p.Id == customer.PersonId)).FirstOrDefault();
        var user = await unitOfWork.GetRepository<ApplicationUser>().GetAsync(customer.UserId);
        var stage = (await unitOfWork.GetRepository<CustomerStage>()
            .GetAllIncludingDeletedAsync(s => s.Id == customer.CustomerStageId)).FirstOrDefault();

        int? lastStatusAction = null;
        int? lastStatusSource = null;
        DateTime? lastStatusAt = null;
        if (loggedNow.HasValue)
        {
            lastStatusAction = (int)loggedNow.Value;
            lastStatusSource = (int)CustomerStatusSource.Manual;
            lastStatusAt = DateTime.UtcNow;
        }
        else
        {
            // SPEC 66: the last status ignores stage changes, it explains why the customer is active or inactive.
            var last = (await unitOfWork.GetRepository<CustomerStatusHistory>()
                    .GetAllIncludingDeletedAsync(h => h.CustomerId == customer.Id && h.GcRecord == 0
                        && h.Action != CustomerStatusAction.StageChanged))
                .OrderByDescending(h => h.OccurredAt)
                .FirstOrDefault();
            if (last is not null)
            {
                lastStatusAction = (int)last.Action;
                lastStatusSource = (int)last.Source;
                lastStatusAt = last.OccurredAt;
            }
        }

        return new CustomerResponseDto
        {
            Id = customer.Id,
            GcRecord = customer.GcRecord,
            CompanyId = customer.CompanyId,
            PersonId = customer.PersonId,
            UserId = customer.UserId,
            CustomerCode = customer.CustomerCode,
            CustomerStageId = customer.CustomerStageId,
            CustomerStageCode = stage?.Code ?? string.Empty,
            CustomerStageName = stage?.Name ?? string.Empty,
            PersonName = person is null ? string.Empty : BuildPersonName(person),
            UserEmail = user?.Email ?? string.Empty,
            IsActive = customer.IsActive,
            ActivatedAt = customer.ActivatedAt,
            DeactivatedAt = customer.DeactivatedAt,
            CreatedAt = customer.Created,
            LastStatusAction = lastStatusAction,
            LastStatusSource = lastStatusSource,
            LastStatusAt = lastStatusAt
        };
    }

    internal static string BuildPersonName(Person person)
    {
        if (person.PersonType == PersonType.Legal)
        {
            return person.CommercialName ?? person.FirstName;
        }

        return string.Join(
            ' ',
            new[] { person.FirstName, person.MiddleName, person.LastName, person.SecondLastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
