


using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;



namespace JOIN.Infrastructure.Contexts;



/// <summary>
/// Provides a lightweight database connection context specifically for Dapper.
/// Supports Database Agnosticism by reading the provider from configuration and 
/// returning the appropriate IDbConnection (SqlConnection or NpgsqlConnection).
/// </summary>
public class DapperContext
{
    private readonly IConfiguration _configuration;
    private readonly string? _connectionString;
    private readonly string? _databaseProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DapperContext"/> class.
    /// Extracts connection settings from the injected IConfiguration.
    /// </summary>
    /// <param name="configuration">The application configuration properties.</param>
    public DapperContext(IConfiguration configuration)
    {
        _configuration = configuration;
        _connectionString = _configuration.GetConnectionString("DefaultConnection");
        _databaseProvider = _configuration["DatabaseProvider"];
    }

    /// <summary>
    /// Creates and returns a raw database connection based on the configured provider.
    /// </summary>
    /// <returns>An instance of <see cref="IDbConnection"/> ready to be used by Dapper.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the connection string is missing.</exception>
    public IDbConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("Database connection string is not configured.");
        }

        if (_databaseProvider != null && _databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return new NpgsqlConnection(_connectionString);
        }
        
        // Default to SQL Server if provider is missing or explicitly set to SqlServer
        return new SqlConnection(_connectionString);
    }
}