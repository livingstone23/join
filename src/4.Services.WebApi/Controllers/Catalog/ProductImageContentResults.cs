using JOIN.Application.DTO.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace JOIN.Services.WebApi.Controllers.Catalog;

/// <summary>
/// SPEC 70 — how a product image is served by <c>Products/{id}/images/{imageId}/content</c> and by the
/// <c>Leads/products-lookup</c> thumbnail route: <c>inline</c> (rendered, not downloaded), private cache for an hour
/// and <c>nosniff</c>, with the content type the server fixed from the file signature.
/// </summary>
public static class ProductImageContentResults
{
    public static IActionResult ToInlineImage(this ControllerBase controller, ProductImageContentResult image)
    {
        var headers = controller.Response.Headers;
        headers.CacheControl = "private, max-age=3600";
        headers.XContentTypeOptions = "nosniff";
        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(image.FileName);
        headers.ContentDisposition = disposition.ToString();
        // No file download name: FileResult would otherwise send "attachment".
        return controller.File(image.Content, image.ContentType);
    }
}
