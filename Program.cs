using System.Data;
using ModelContextProtocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Data.SqlClient;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);

        // Add configuration
        builder.Configuration
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
            .AddEnvironmentVariables();

        // Add logging
        builder.Services.AddLogging(configure =>
        {
            configure.AddConsole();
            configure.AddDebug();
        });

        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();
        // Configure database connection
        builder.Services.AddSingleton<IDbConnection>(serviceProvider =>
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            var provider = configuration?["DatabaseSettings:Provider"] ?? "SQLite";
            if (configuration != null)
            {
                var commandTimeout = configuration.GetValue<int>("DatabaseSettings:CommandTimeout", 30);
            }

            logger.LogInformation("Initializing {Provider} database connection", provider);

            if (configuration != null)
            {
                IDbConnection connection = provider.ToUpper() switch
                {
                    "SQLITE" => new SqliteConnection(configuration.GetConnectionString("DefaultConnection")),
                    "SQLSERVER" => new SqlConnection(configuration.GetConnectionString("SqlServerConnection")),
                    "POSTGRESQL" => new NpgsqlConnection(configuration.GetConnectionString("PostgreSqlConnection")),
                    "MYSQL" => new MySqlConnection(configuration.GetConnectionString("MySqlConnection")),
                    _ => new SqliteConnection(configuration.GetConnectionString("DefaultConnection"))
                };

                connection.Open();
                logger.LogInformation("Database connection established successfully");
                return connection;
            }
            else
            {
                return null;
            }
        });
        builder.Services.AddSingleton<IConfiguration>(serviceProvider =>
            serviceProvider.GetRequiredService<IConfiguration>());
        builder.Services.AddSingleton<ILogger>(serviceProvider =>
            serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseTools"));

        var app = builder.Build();

        await app.RunAsync();
    }
}