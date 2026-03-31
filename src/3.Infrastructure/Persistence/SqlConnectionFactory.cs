using System.Data;
using JOIN.Application.Interface;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Npgsql;



namespace JOIN.Infrastructure.Persistence;



/// <summary>
/// Implementation of the connection factory that supports multiple database engines.
/// Uses Primary Constructors for dependency injection.
/// </summary>
public class SqlConnectionFactory(IConfiguration configuration) : ISqlConnectionFactory
{
    /// <summary>
    /// Creates a new connection based on the 'DatabaseProvider' specified in appsettings.
    /// </summary>
    /// <returns>A SQL Server or PostgreSQL connection.</returns>
    /// <exception cref="InvalidOperationException">Thrown when connection string is missing.</exception>
    /// <exception cref="NotSupportedException">Thrown when an unsupported provider is specified.</exception>
    public IDbConnection CreateConnection()
    {
        // Read the configured provider (defaults to SqlServer if not found)
        var provider = configuration.GetValue<string>("DatabaseProvider") ?? "SqlServer";
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The 'DefaultConnection' connection string is not configured in appsettings.");
        }

        // Return the specific connection based on the chosen provider
        return provider.ToLowerInvariant() switch
        {
            "postgresql" => new NpgsqlConnection(connectionString),
            "sqlserver" => new SqlConnection(connectionString),
            _ => throw new NotSupportedException($"The database provider '{provider}' is not supported in JOIN CRM.")
        };
    }
}