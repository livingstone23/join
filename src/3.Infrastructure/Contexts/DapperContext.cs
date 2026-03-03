


using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;



namespace JOIN.Infrastructure.Contexts;



/// <summary>
/// Factory class responsible for creating lightweight, direct database connections.
/// Used exclusively for high-performance read-only queries (Dapper), bypassing EF Core's Change Tracker.
/// </summary>
public class DapperContext
{
    private readonly string? _connectionString;

    public DapperContext(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
    }

    /// <summary>
    /// Creates a new SQL connection instance. 
    /// The caller is responsible for disposing of the connection.
    /// </summary>
    public IDbConnection CreateConnection() => new SqlConnection(_connectionString);
}