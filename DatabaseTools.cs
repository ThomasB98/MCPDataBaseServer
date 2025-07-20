using System.ComponentModel;
using System.Data;
using System.Text.Json;
using ModelContextProtocol.Server;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MCPDataBaseMSSQLServer;

[McpServerToolType]
public static class DatabaseTools
{
    [McpServerTool ,  Description("Execute a SQL SELECT query on the database and return results.")]
    public static async Task<string> ExecuteQuery(
        IDbConnection connection,
        IConfiguration configuration,
        ILogger logger,
        [Description("The SQl SELECT query to execute.")]string query,
        [Description("Optional parameters for the query (JSON object).")]string parameters = "{}")
    {
        try
        {
            // Security validation
            var allowedCommands = configuration.GetSection("Security:AllowedCommands").Get<string[]>() ?? Array.Empty<string>();
            if (!allowedCommands.Any(cmd => query.Trim().StartsWith(cmd, StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogWarning("Query blocked: Command not in allowed list. Query: {Query}", query);
                return "Query blocked: Command not allowed.";
            }

            var queryLimit = configuration.GetValue<int>("DatabaseSettings:QueryExecutionLimit", 1000);
            logger.LogDebug("Executing query with limit {Limit}: {Query}", queryLimit, query);

            var queryParams = new DynamicParameters();
            if (!string.IsNullOrEmpty(query) && parameters != "{}")
            {
                var paramDict = JsonSerializer.Deserialize<Dictionary<string, object>>(parameters);
                foreach (var param in paramDict!)
                {
                    queryParams.Add(param.Key, param.Value);
                    logger.LogDebug("Added parameter: {Key}", param.Key);
                }
            }
            
            // Execute query with timeout from configuration
            var commandTimeout = configuration.GetValue<int>("DatabaseSettings:CommandTimeout", 30);
            var results = await connection.QueryAsync(query, queryParams, commandTimeout: commandTimeout);
                
            var resultList = results.ToList();
            logger.LogInformation("Query executed successfully. Returned {Count} rows", resultList.Count);

            if (!results.Any())
            {
                return "No results found.";
            }
            
            var limitedResults = resultList.Take(queryLimit);
            var jsonResults = JsonSerializer.Serialize(results, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
                
            return $"Query executed successfully. Results:\n{jsonResults}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Query execution failed: {Query}", query);
            return $"Query execution failed: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get information about database tables and their structure.")]
    public static async Task<string> getTableSchema(
        IDbConnection connection,
        IConfiguration configuration,
        ILogger logger,
        [Description("Name of the table to inspect. Leave empty to list all tables.")]string tableName)
    {
        try
        {
            var provider = configuration["DatabaseSettings:Provider"] ?? "SQLite";
            logger.LogDebug("Getting schema information for provider: {Provider}, table: {Table}", provider, tableName);
            
            if (string.IsNullOrEmpty(tableName))
            {
                // List all tables based on database provider
                var tablesQuery = provider.ToUpper() switch
                {
                    "SQLITE" => "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name",
                    "SQLSERVER" => "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME",
                    "POSTGRESQL" => "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name",
                    "MYSQL" => "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() ORDER BY table_name",
                    _ => "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name"
                };

                var tables = await connection.QueryAsync<string>(tablesQuery);
                logger.LogInformation("Retrieved {Count} tables", tables.Count());
                    
                return $"Available tables:\n{string.Join("\n", tables)}";
            }
            else
            {
                var restrictedTables = configuration.GetSection("Security:RestrictedTables").Get<string[]>() ?? Array.Empty<string>();
                    if (restrictedTables.Contains(tableName, StringComparer.OrdinalIgnoreCase))
                    {
                        logger.LogWarning("Access denied to restricted table: {Table}", tableName);
                        return "Access denied: Table is restricted.";
                    }

                    // Get table schema based on database provider
                    var schemaQuery = provider.ToUpper() switch
                    {
                        "SQLITE" => "PRAGMA table_info(@tableName)",
                        "SQLSERVER" => @"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT 
                                       FROM INFORMATION_SCHEMA.COLUMNS 
                                       WHERE TABLE_NAME = @tableName ORDER BY ORDINAL_POSITION",
                        "POSTGRESQL" => @"SELECT column_name, data_type, is_nullable, column_default 
                                        FROM information_schema.columns 
                                        WHERE table_name = @tableName ORDER BY ordinal_position",
                        "MYSQL" => @"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT 
                                   FROM INFORMATION_SCHEMA.COLUMNS 
                                   WHERE TABLE_NAME = @tableName ORDER BY ORDINAL_POSITION",
                        _ => "PRAGMA table_info(@tableName)"
                    };

                    var schema = await connection.QueryAsync(schemaQuery, new { tableName });
                    
                    var schemaJson = JsonSerializer.Serialize(schema, new JsonSerializerOptions 
                    { 
                        WriteIndented = true 
                    });
                    
                    logger.LogInformation("Retrieved schema for table: {Table}", tableName);
                    return $"Schema for table '{tableName}':\n{schemaJson}";
            }
        }
        catch(Exception ex)
        {
            logger.LogError(ex, "Schema retrieval failed for table: {Table}", tableName);
            return $"Schema retrieval failed: {ex.Message}";
        }
    }

    [McpServerTool,Description("Execute a SQL command (INSERT, UPDATE, DELETE) on the database.")]
    public static async Task<string> ExecuteNonQuery(
        IDbConnection connection,
        [Description("The SQL command to execute (INSERT, UPDATE, DELETE).")] string command,
        [Description("Optional parameters for the command (JSON object).")] string parameters = "{}")
    {
        try
        {
            // Prevent SELECT statements for safety
            if (command.Trim().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                return "Use ExecuteQuery for SELECT statements.";
            }
            var queryParams = new DynamicParameters();
            if (!string.IsNullOrEmpty(command) && parameters != "{}")
            {
                var paramDict = JsonSerializer.Deserialize<Dictionary<string, object>>(parameters);
                foreach (var param in paramDict!)
                {
                    queryParams.Add(param.Key, param.Value);
                }
            }
            var rowsAffected = await connection.ExecuteAsync(command, queryParams);
                
            return $"Command executed successfully. {rowsAffected} row(s) affected.";
        }catch(Exception ex)
        {
            return $"Command execution failed: {ex.Message}";
        }
    }

    [McpServerTool, Description("Create a backup of a table or entire database.")]
    public static async Task<string> BackupTable(
        IDbConnection connection,
        [Description("Name of the table to backup.")] string tableName,
        [Description("Path where to save the backup file.")] string backupPath)
    {
        try
        {
            var data = await connection.QueryAsync<string>(
                $"SELECT * FROM {tableName}");
            var jsonBackup = JsonSerializer.Serialize(data, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            await System.IO.File.WriteAllTextAsync(backupPath, jsonBackup);
                
            return $"Table '{tableName}' backed up successfully to '{backupPath}'";
        }
        catch (Exception ex)
        {
            return $"Backup failed: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get database connection information and status.")]
    public static Task<string> GetConnectionInfo(IDbConnection connection)
    {
        try
        {
            var info = new
            {
                Database = connection.Database,
                ConnectionString = connection.ConnectionString,
                State = connection.State.ToString(),
                ConnectionTimeout = connection.ConnectionTimeout
            };

            var jsonInfo = JsonSerializer.Serialize(info, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });

            return Task.FromResult($"Database connection info:\n{jsonInfo}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Failed to get connection info: {ex.Message}");
        }
    }
}