namespace AlUmmahNow.Tests.Infrastructure;

/// <summary>
/// A test that needs a real SQL Server (T-SQL upgrade scripts, migrations). Skipped unless
/// <c>AUN_TEST_SQLSERVER</c> holds a connection string to a server where the test may create and drop
/// databases, e.g. a local container:
/// <c>docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=... -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest</c>.
/// CI has no SQL Server, so these run locally.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string Variable = "AUN_TEST_SQLSERVER";

    public static string? ConnectionString => Environment.GetEnvironmentVariable(Variable);

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            Skip = $"Set {Variable} to a SQL Server connection string to run.";
        }
    }
}
