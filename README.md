# MCPDataBaseMSSQLServer

A small, self-contained .NET 8 console application that hosts a [Model-Context-Protocol (MCP)](https://github.com/amusev) server able to talk to a relational database.  
Although the project name references **MSSQL**, the code has been abstracted so you can easily switch between several popular database engines (SQLite, SQL Server, PostgreSQL and MySQL) just by changing the configuration file – no code changes required.

---

## What the project does

1. **Bootstraps an MCP server** – The `Program.cs` entry point wires the MCP services and exposes them over STDIO so the executable can be embedded in other host processes (e.g. editors).
2. **Initialises a database connection** – At start-up the application reads `appsettings.json` to decide which ADO.NET provider to use and opens the connection (`SqliteConnection`, `SqlConnection`, `NpgsqlConnection` or `MySqlConnection`).
3. **Provides basic logging & validation** – Logging is enabled via `Microsoft.Extensions.Logging` and optional query-validation rules are read from configuration.
4. **Offers a playground for experimenting with Dapper & MCP** – Because it is framework-agnostic you can point the same binary at different database back-ends to test behaviour and performance.

---

## Quick start

### Prerequisites

* .NET 8 SDK (download from https://dotnet.microsoft.com)
* One of the supported databases running locally (or reachable via network)

### Build & run

```bash
# Restore packages & compile
$ dotnet build

# Run with default settings (SQLite / sample.db)
$ dotnet run --project MCPDataBaseMSSQLServer.csproj
```

When the application starts you should see logging similar to:

```
info: Program[0]
      Initializing SQLITE database connection
info: Program[0]
      Database connection established successfully
```

---

## Changing the database provider

All runtime behaviour is driven by **`appsettings.json`** (or the environment-specific variant such as `appsettings.Development.json`).

```jsonc
"DatabaseSettings": {
  "Provider": "SQLite",        // <-- Change to SQLServer | PostgreSQL | MySQL
  "CommandTimeout": 30,
  "MaxRetryAttempts": 3
},
"ConnectionStrings": {
  "DefaultConnection": "Data Source=sample.db",
  "SqlServerConnection": "Server=localhost;Database=TestDB;Trusted_Connection=true;TrustServerCertificate=true;",
  "PostgreSqlConnection": "Host=localhost;Database=testdb;Username=postgres;Password=password;",
  "MySqlConnection": "Server=localhost;Database=testdb;Uid=root;Pwd=password;"
}
```

Steps:

1. Set `DatabaseSettings:Provider` to the desired engine.
2. Update the matching connection string.
3. Re-run the application.

> NOTE: The application opens the connection at start-up. If the string is invalid or the server cannot be reached it will throw immediately.

---

## Security & query validation

The `Security` section allows you to restrict what the MCP server may execute:

```jsonc
"Security": {
  "AllowedCommands": [ "SELECT", "INSERT", "UPDATE", "DELETE" ],
  "RestrictedTables": [ "sys_users", "admin_settings" ],
  "EnableQueryValidation": true
}
```

* **AllowedCommands** – Whitelist of SQL verbs permitted.
* **RestrictedTables** – Blacklist of tables that must never be referenced.
* **EnableQueryValidation** – Toggle validation on/off.

Adjust these lists to fit the policies of your environment.

---

## Typical changes you may need to make

1. **Connection strings** – Point to your own databases.
2. **Provider selection** – Change from the default SQLite to your chosen engine.
3. **Timeouts & retry logic** – Tune `CommandTimeout` and `MaxRetryAttempts` based on workload.
4. **Logging level** – Set `Logging.LogLevel.*` to control verbosity.
5. **Security rules** – Expand or tighten the `Security` section.
6. **Packaging** – If you want a single-file executable run:
   ```bash
   dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
   ```

---

## Project structure

```
│   Program.cs               # Bootstrap & composition root
│   appsettings.json         # Main configuration file
│   MCPDataBaseMSSQLServer.csproj
└───README.md                # You are here
```

---

## Contributing

Pull requests are welcome!  If you have feature requests or run into problems, please open an issue.

---

## License

This project is released under the MIT License.
