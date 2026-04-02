


using JOIN.Application.Common;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Commands;



/// <summary>
/// Command to register a new Customer in the system.
/// Carries the complete hierarchical payload, including addresses and contacts.
/// </summary>
public record CreateCustomerCommand : IRequest<Response<Guid>>
{
	/// <summary>
	/// Categorizes the customer as Physical (Natural Person) or Legal (Company/Organization).
	/// </summary>
	public string PersonType { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the first name of the customer.
	/// </summary>
	public string FirstName { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the middle name of the customer.
	/// </summary>
	public string? MiddleName { get; init; }

	/// <summary>
	/// Gets or sets the first surname of the customer.
	/// </summary>
	public string LastName { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the second surname of the customer.
	/// </summary>
	public string? SecondLastName { get; init; }

	/// <summary>
	/// Gets or sets the business or trade name.
	/// </summary>
	public string? CommercialName { get; init; }

	/// <summary>
	/// Gets or sets the foreign key for the identification document type.
	/// </summary>
	public Guid IdentificationTypeId { get; init; }

	/// <summary>
	/// Gets or sets the unique identification number (ID Card, Tax ID).
	/// </summary>
	public string IdentificationNumber { get; init; } = string.Empty;

	/// <summary>
	/// Gets or sets the optional list of addresses to create together with the customer.
	/// </summary>
	public IReadOnlyCollection<CreateCustomerAddressDto>? Addresses { get; init; }

	/// <summary>
	/// Gets or sets the optional list of contacts to create together with the customer.
	/// </summary>
	public IReadOnlyCollection<CreateCustomerContactDto>? Contacts { get; init; }

	/// <summary>
	/// Nested DTO representing an address for customer creation.
	/// </summary>
	public record CreateCustomerAddressDto
	{
		/// <summary>
		/// Primary address line.
		/// </summary>
		public string AddressLine1 { get; init; } = string.Empty;

		/// <summary>
		/// Secondary address line.
		/// </summary>
		public string? AddressLine2 { get; init; }

		/// <summary>
		/// Postal code.
		/// </summary>
		public string ZipCode { get; init; } = string.Empty;

		/// <summary>
		/// Foreign key to street type catalog.
		/// </summary>
		public Guid StreetTypeId { get; init; }

		/// <summary>
		/// Foreign key to country catalog.
		/// </summary>
		public Guid CountryId { get; init; }

		/// <summary>
		/// Foreign key to region catalog.
		/// </summary>
		public Guid? RegionId { get; init; }

		/// <summary>
		/// Foreign key to province catalog.
		/// </summary>
		public Guid ProvinceId { get; init; }

		/// <summary>
		/// Foreign key to municipality catalog.
		/// </summary>
		public Guid MunicipalityId { get; init; }

		/// <summary>
		/// Indicates whether this address is the default one.
		/// </summary>
		public bool IsDefault { get; init; }
	}

	/// <summary>
	/// Nested DTO representing a contact method for customer creation.
	/// </summary>
	public record CreateCustomerContactDto
	{
		/// <summary>
		/// Contact category (e.g., MobilePhone, PrimaryEmail, WhatsApp).
		/// </summary>
		public string ContactType { get; init; } = string.Empty;

		/// <summary>
		/// Contact information value.
		/// </summary>
		public string ContactValue { get; init; } = string.Empty;

		/// <summary>
		/// Indicates whether this contact is the primary one.
		/// </summary>
		public bool IsPrimary { get; init; }

		/// <summary>
		/// Optional comments or instructions for the contact.
		/// </summary>
		public string? Comments { get; init; }
	}
}



