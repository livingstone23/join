using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.ProductAttachmentSettings.Queries;

/// <summary><c>GET /ProductAttachmentSettings</c> (SPEC 70): the company's limits, or <c>PRODUCT_ATTACHMENT_SETTINGS_NOT_CONFIGURED</c>.</summary>
public sealed record GetProductAttachmentSettingsQuery : IRequest<Response<ProductAttachmentSettingsDto>>;
