# ScriptRunner User Guide

This guide walks through using ScriptRunner interactively, including the schema create script feature, then covers [CLI mode](#5-non-interactive-cli-mode) and [calling ScriptRunner from your own program](#6-calling-scriptrunner-from-your-own-program). The screens below are **simulated terminal output** (text mockups of what Spectre.Console renders), not real screen captures — they're here to show the shape and order of each prompt.


---

## 1. Starting the tool

Run the built executable, or `dotnet run --project ./ScriptRunner/Dnp.ScriptRunner.csproj`, with no arguments to start the interactive flow.

```
───────────────────── Dnp.ScriptRunner ©2026 Doughnuts Publishing ─────────────────────

? Select database type
> PostgreSQL
  SqlServer
  Sqlite
  MySQL
  Oracle
  DB2
```

Use the arrow keys to pick a provider and press Enter. This guide continues with **PostgreSQL** as the example, but the same prompts appear for every supported provider.

---

## 2. Enter or select a connection string

The first time you connect to a given provider, you're asked for a connection string directly:

```
───────────────────── Dnp.ScriptRunner ©2026 Doughnuts Publishing ─────────────────────

? Enter connection string
> Host=localhost;Username=app;Password=****;Database=orders_db
```

On later runs, any connection strings you've used before for that provider are offered as a pick-list, so you don't have to retype them:

```
───────────────────── Dnp.ScriptRunner ©2026 Doughnuts Publishing ─────────────────────

? Select a saved connection or create new
> Host=localhost;Username=app;Password=****;Database=orders_db
  Host=reporting.internal;Username=ro_user;Password=****;Database=orders_db
  <New connection...>
```

---

## 3. Choose what to do next

This is the new fork in the flow. Right after the connection string is resolved, you're asked:

```
───────────────────── Dnp.ScriptRunner ©2026 Doughnuts Publishing ─────────────────────

Connection opened.

? Generate a schema create script for this connection? [y/n] (n):
```

- Answer **`n`** (or press Enter, since No is the default) to go run scripts against the database as usual — see [4A. Running scripts](#4a-running-scripts).
- Answer **`y`** to generate a schema/DDL script instead — see [4B. Generating a schema create script](#4b-generating-a-schema-create-script).

---

## 4A. Running scripts

If you answered **No** above, you're asked which folder holds the scripts to run:

```
? Select a saved scripts directory or create new
> C:\scripts\orders-migrations
  C:\scripts\reporting
  <New directory...>
```

Then which files in that folder to run, with checkboxes:

```
? Select files to include (use <space> to toggle). You can select .sql and .txt
> [x] 001_create_tables.sql
  [x] 002_seed_lookup_data.sql
  [ ] 003_optional_backfill.sql
  [x] 004_add_indexes.sql
```

ScriptRunner then queues each selected file, opens the connection, and streams progress as it runs:

```
(1/3) Queuing 001_create_tables.sql
(2/3) Queuing 002_seed_lookup_data.sql
(3/3) Queuing 004_add_indexes.sql
Opening connection...
Connection opened.
Executing SQL file: 001_create_tables.sql
 OK Statement 1/4
 OK Statement 2/4
 OK Statement 3/4
 OK Statement 4/4
Executing SQL file: 002_seed_lookup_data.sql
 OK Statement 1/1
Executing SQL file: 004_add_indexes.sql
 OK Statement 1/2
 OK Statement 2/2

All selected scripts processed. 7 statement(s) succeeded, 0 failed in 1.4s.
Run log written to C:\scripts\orders-migrations\script-run-results-20260915_101512.log

? Do you want to open the run results now? [y/n] (y):
```

If a statement fails, the error is shown and you choose how to carry on:

```
 ERROR Statement 2/4: relation "orders" already exists
> Continue
  Skip File
  Stop
```

- **Continue** runs the next statement.
- **Skip File** skips the rest of the current `.sql` file and moves on to the next file.
- **Stop** ends the run; the summary reports it as stopped.

---

## 4B. Generating a schema create script

If you answered **Yes** to "Generate a schema create script for this connection?", you're immediately asked one more question:

```
? Include data (INSERT statements for each table) in the script as well?  [y/n] (n):
```

- **No** → the generated script contains schema only: `CREATE SCHEMA` (where the provider needs one), `CREATE TABLE`/constraints/indexes, views, functions/procedures, and triggers.
- **Yes** → the script additionally includes an `INSERT INTO` statement for every existing row in every table.

### Choosing an output directory

```
? Select a saved directory to save the schema script or create new
> C:\scripts\orders-migrations
  C:\exports\db-snapshots
  <New directory...>
```

If you type a path that doesn't exist yet, ScriptRunner creates it for you automatically — you don't need to create the folder first:

```
? Output directory for schema script (full path) (C:\Users\dhunt):
> C:\exports\2026-09-15-snapshot
```

### Generation and output

```
Connection opened.
Generating schema create script...
Generating data insert script...

Schema create script written to C:\exports\2026-09-15-snapshot\schema-create-PostgreSQL-20260915_101820.sql
```

ScriptRunner then exits — this mode doesn't run any scripts, it only produces the file above.

### What the generated file looks like

A trimmed example of the output for PostgreSQL (with data included):

```sql
-- Schema create script generated by ScriptRunner
-- Generated: 2026-09-15 10:18:20

CREATE SCHEMA IF NOT EXISTS "public";

CREATE TABLE "public"."customers" (
    "id" integer NOT NULL,
    "name" varchar(200) NOT NULL,
    "created_at" timestamp without time zone DEFAULT now()
);

ALTER TABLE "public"."customers" ADD CONSTRAINT "customers_pkey" PRIMARY KEY (id);

CREATE INDEX "idx_customers_name" ON "public"."customers" USING btree (name);

CREATE OR REPLACE VIEW "public"."active_customers" AS
SELECT id, name FROM customers WHERE deleted_at IS NULL;

CREATE OR REPLACE FUNCTION public.customer_count()
 RETURNS integer
 LANGUAGE sql
AS $function$
  SELECT count(*)::int FROM customers;
$function$;

-- Data insert script generated by ScriptRunner
-- Generated: 2026-09-15 10:18:21

INSERT INTO "public"."customers" ("id", "name", "created_at") VALUES (1, 'Acme Co', '2025-01-04 09:15:00.000');
INSERT INTO "public"."customers" ("id", "name", "created_at") VALUES (2, 'Doughnuts Publishing', '2025-03-11 14:02:33.000');
```

For SQL Server, each view/function/procedure/trigger is followed by a `GO` line, since T-SQL requires those statements to be alone in their batch:

```sql
CREATE OR ALTER VIEW [dbo].[ActiveCustomers] AS
SELECT Id, Name FROM Customers WHERE DeletedAt IS NULL;
GO
```

---

## 5. Non-interactive (CLI) mode

Running ScriptRunner with three or more arguments (database type, connection string, scripts directory, then optionally the files to run) skips every prompt above, so it can be started by CI/CD pipelines, scheduled tasks or any other program:

```bash
# Every .sql/.txt file in the folder, in name order
./ScriptRunner/bin/Release/net10.0/win-x64/Dnp.ScriptRunner.exe PostgreSQL "Host=localhost;Username=app;Password=pass;Database=mydb" "C:\scripts"

# Only the named files, in the order given
./ScriptRunner/bin/Release/net10.0/win-x64/Dnp.ScriptRunner.exe PostgreSQL "Host=localhost;Username=app;Password=pass;Database=mydb" "C:\scripts" 001_create_tables.sql 004_add_indexes.sql
```

In CLI mode:

- No file-selection, error or "open the results" prompts are shown, so the process never waits for input.
- The run stops at the first failed statement.
- The run log is still written to the scripts directory.
- The exit code tells the caller what happened: `0` success, `1` statement failure or cancellation, `2` unknown database type, `3` scripts directory not found, `5` fatal error (such as a connection failure).

Schema generation is only offered interactively; from code, use `SqlScriptRunner.GenerateSchemaScript` (see below).

---

## 6. Calling ScriptRunner from your own program

Everything the console app does is available as a .NET library, `Dnp.ScriptRunner.Core`, with no prompts or console output. Reference `ScriptRunner.Core/Dnp.ScriptRunner.Core.csproj` (or a package built with `dotnet pack ScriptRunner.Core`) and call:

```csharp
using Dnp.ScriptRunner;

var result = await SqlScriptRunner.RunAsync(
    new ScriptRunOptions(DatabaseProvider.SqlServer, connectionString)
    {
        ScriptDirectory = @"C:\scripts\orders-migrations",
        Files = { "001_create_tables.sql", "004_add_indexes.sql" },  // omit to run the whole folder
        OnError = failed => ScriptErrorAction.SkipFile,              // the same choices as the interactive prompt
        OnEvent = e => logger.LogInformation(e.Message)
    });

if (!result.Success) { /* result.Failures lists the file, statement number, SQL and error */ }

// Schema create script, the same output as section 4B
string ddl = SqlScriptRunner.GenerateSchemaScript(DatabaseProvider.SqlServer, connectionString, includeData: true);
```

`RunSqlAsync` runs a string of SQL instead of files. See [ScriptRunner.Core/README.md](ScriptRunner.Core/README.md) for the full API.

---

## Tips

- Answering **No** to "Generate a schema create script?" is the default (just press Enter) — existing behavior is unchanged if you never opt in.
- A saved connection string, scripts directory, or schema-output directory only needs to be typed once; ScriptRunner remembers it per database type in `script-runner-settings.json` next to the executable.
- If a particular database object (a view, function, procedure, or trigger) can't be scripted — for example, due to a permissions issue — the generated file contains a `-- Failed to generate DDL for ...` comment in its place instead of stopping the whole run.
- Data scripting is best suited to small-to-medium reference/lookup tables. Large transactional tables will produce a correspondingly large `.sql` file, and rows are written in schema order rather than foreign-key dependency order.
