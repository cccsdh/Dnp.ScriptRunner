# Dnp.ScriptRunner.Core

**Embeddable SQL script runner for .NET.** Run `.sql` files, `.txt` script lists and inline SQL against
**PostgreSQL, SQL Server, MySQL, Oracle, IBM DB2 and SQLite** directly from your own application, and generate
schema create scripts (DDL, optionally with data) from a live database.

This is the same engine that powers the [ScriptRunner](https://cccsdh.github.io/Dnp.ScriptRunner/) console app's
interactive UI and command-line mode, packaged as a class library with no console or UI dependency, so you can call it
from an ASP.NET Core startup, a worker service, a deployment tool, an integration-test fixture or any other .NET program.

## Install

Reference the project from source:

```bash
dotnet add reference path/to/Dnp.ScriptRunner/ScriptRunner.Core/Dnp.ScriptRunner.Core.csproj
```

Or build a NuGet package and add it from a local feed:

```bash
dotnet pack ScriptRunner.Core -c Release -o ./nupkg
dotnet add package Dnp.ScriptRunner.Core --source ./nupkg
```

PostgreSQL, SQL Server, MySQL, Oracle and SQLite drivers come with the package. **DB2 only:** IBM ships a separate
driver per OS, so add the one for your platform yourself (`Net.IBM.Data.Db2` on Windows, `Net.IBM.Data.Db2-lnx` on
Linux). Applications that never use DB2 don't need it.

## Run a folder of scripts

```csharp
using Dnp.ScriptRunner;

var result = await SqlScriptRunner.RunAsync(
    new ScriptRunOptions(DatabaseProvider.PostgreSQL, "Host=localhost;Username=app;Password=pass;Database=mydb")
    {
        ScriptDirectory = "/srv/app/sql"   // every top-level .sql and .txt file, in name order
    });

if (!result.Success)
    throw new Exception($"{result.StatementsFailed} statement(s) failed: {result.Failures[0].Error}");
```

## Run specific files, with progress and a log

```csharp
using var log = new StreamWriter("migrate.log");

var options = new ScriptRunOptions(DatabaseProvider.SqlServer, connectionString)
{
    ScriptDirectory = @"C:\deploy\sql",
    Files = { "01_schema.sql", "release_2026_10.txt" },     // relative to ScriptDirectory, or absolute
    Log = log,                                              // timestamped run log
    OnEvent = e => logger.LogInformation("{Kind}: {Message}", e.Kind, e.Message),
    OnError = failed => failed.Error!.Contains("already exists")
        ? ScriptErrorAction.Continue                        // tolerate re-runs
        : ScriptErrorAction.Stop
};

ScriptRunResult result = await SqlScriptRunner.RunAsync(options, cancellationToken);
Console.WriteLine($"{result.StatementsSucceeded} ok, {result.StatementsFailed} failed in {result.Elapsed}");
```

## Run inline SQL

```csharp
await SqlScriptRunner.RunSqlAsync(
    new ScriptRunOptions(DatabaseProvider.Sqlite, "Data Source=app.db"),
    """
    CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);
    GO
    INSERT OR IGNORE INTO Settings VALUES ('version', '1');
    """);
```

## Generate a schema create script

```csharp
string ddl = SqlScriptRunner.GenerateSchemaScript(DatabaseProvider.MySQL, connectionString, includeData: true);
File.WriteAllText("schema.sql", ddl);
```

## API reference

| Member | Purpose |
|---|---|
| `SqlScriptRunner.RunAsync(options, ct)` | Open a connection and run `options.Files`, or every script in `options.ScriptDirectory`. |
| `SqlScriptRunner.RunSqlAsync(options, sql, ct)` | Run a block of SQL text. Embedded file tags resolve against `options.ScriptDirectory`. |
| `SqlScriptRunner.GenerateSchemaScript(provider, conn, includeData)` | Return DDL for schemas, tables, views, functions, procedures and triggers, plus optional `INSERT`s. |
| `SqlScriptRunner.GetScriptFiles(directory)` | The `.sql`/`.txt` files a folder run would use, in order. |
| `SqlScriptRunner.TryParseProvider(text, out provider)` | Case-insensitive provider name parsing, handy for config files and CLI arguments. |

**`ScriptRunOptions`**

| Property | Default | Description |
|---|---|---|
| `Provider`, `ConnectionString` | (constructor) | Target database. |
| `ScriptDirectory` | `null` | Script folder; base for relative `Files` and embedded file tags. |
| `Files` | empty | Scripts to run in order. Empty means the whole `ScriptDirectory`. |
| `ErrorAction` | `Stop` | `Continue`, `SkipFile` or `Stop` after a failed statement. |
| `OnError` | `null` | Per-failure callback returning a `ScriptErrorAction`; overrides `ErrorAction`. |
| `OnEvent` | `null` | Progress callback (`ConnectionOpened`, `SqlFileStarted`, `StatementSucceeded`, `StatementFailed`, `Warning`, ...). |
| `Log` | `null` | `TextWriter` that receives a timestamped line per event. |
| `EmbeddedFileMarkers` | built-in `<DnPJson>`, `<DnPXml>`, `<DnPTxt>` | Custom embedded-file tags. |
| `EnableEmbeddedTypeDetection` | `true` | Detect JSON/XML inside `<DnPTxt>` files. |

**`ScriptRunResult`**: `Success`, `StatementsSucceeded`, `StatementsFailed`, `Failures` (file, statement number,
SQL and error for each), `Stopped`, `ExecutedFiles`, `Elapsed`.

**Behavior notes**

- Statements are separated by lines containing only `GO` (any provider).
- `.txt` files are script lists: one path per line, `>>` for a nested list. Each file runs at most once per run.
- Statement failures are reported in the result, not thrown. Connection failures throw the provider's exception, and
  cancelling the token throws `OperationCanceledException`.
- The library never prompts, never reads `script-runner-settings.json` and writes nothing to disk except your `Log`.

Licensed under the MIT License. Source, console app and full documentation:
<https://github.com/cccsdh/Dnp.ScriptRunner>.
