using System.Data;
using Dapper;

namespace JOIN.Application.UseCases.Catalog.Products.Common;

/// <summary>SQL shared by the image and document queries of a product (SPEC 70).</summary>
internal static class ProductAttachmentQuerySql
{
    /// <summary><c>true</c> when the product is a live product of the company.</summary>
    public static async Task<bool> ProductExistsAsync(IDbConnection connection, Guid productId, Guid companyId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1
            FROM catalog.products p
            WHERE p.Id = @ProductId
              AND p.CompanyId = @CompanyId
              AND p.GcRecord = 0;
            """;

        return await connection.QueryFirstOrDefaultAsync<int?>(new CommandDefinition(
            sql, new { ProductId = productId, CompanyId = companyId }, cancellationToken: cancellationToken)) is not null;
    }

    /// <summary>Where a stored file lives, for the content and download queries.</summary>
    internal sealed class FileRow
    {
        public string Path { get; set; } = string.Empty;
        public int StorageProvider { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
    }
}
