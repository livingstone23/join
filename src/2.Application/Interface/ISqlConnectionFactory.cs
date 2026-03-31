using System.Data;



namespace JOIN.Application.Interface;



/// <summary>
/// Factory to create read-only database connections (Dapper) 
/// agnostic to the database engine (SQL Server or PostgreSQL).
/// </summary>
public interface ISqlConnectionFactory
{
    /// <summary>
    /// Creates and returns an open database connection.
    /// </summary>
    /// <returns>An instance of IDbConnection.</returns>
    IDbConnection CreateConnection();

}
