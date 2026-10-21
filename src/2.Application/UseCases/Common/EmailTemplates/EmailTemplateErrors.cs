using JOIN.Application.Common;
using JOIN.Application.Services.Email;

namespace JOIN.Application.UseCases.Common.EmailTemplates;

/// <summary>Error responses shared by the email template commands and queries (SPEC 71).</summary>
internal static class EmailTemplateErrors
{
    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(EmailTemplateErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    internal static Response<T> NotFound<T>() =>
        Response<T>.Error(EmailTemplateErrorCodes.EmailTemplateNotFound, ["Email template not found."]);

    internal static Response<T> TypeNotFound<T>() =>
        Response<T>.Error(EmailTemplateErrorCodes.EmailTemplateTypeNotFound, ["Email template type not found or inactive."]);

    internal static Response<T> DuplicateName<T>() =>
        Response<T>.Error(EmailTemplateErrorCodes.DuplicateKey, ["An email template with that name already exists in this company."]);

    internal static Response<T> UnknownVariables<T>(IReadOnlyList<string> variables) =>
        Response<T>.Error(
            EmailTemplateErrorCodes.EmailTemplateUnknownVariable,
            [$"Unknown template variables: {string.Join(", ", variables)}."]);
}
