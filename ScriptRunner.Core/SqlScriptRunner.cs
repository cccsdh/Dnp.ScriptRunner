/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Dnp.ScriptRunner
{
    /// <summary>
    /// Entry point for running SQL scripts and generating schema scripts from your own code. This is the same engine
    /// the ScriptRunner console application uses for its interactive and command-line modes.
    /// </summary>
    public static class SqlScriptRunner
    {
        /// <summary>Returns the top-level .sql and .txt files in a folder, sorted by name (the order a folder run uses).</summary>
        public static IReadOnlyList<string> GetScriptFiles(string directory)
        {
            return Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileName(f))
                .ToList();
        }

        /// <summary>Parses a provider name such as "PostgreSQL" or "sqlserver" (case-insensitive).</summary>
        public static bool TryParseProvider(string? value, out DatabaseProvider provider) =>
            Enum.TryParse(value, ignoreCase: true, out provider) && Enum.IsDefined(provider);

        /// <summary>
        /// Opens a connection and runs the scripts in <see cref="ScriptRunOptions.Files"/> (or every script in
        /// <see cref="ScriptRunOptions.ScriptDirectory"/>), following .txt script lists and nested <c>&gt;&gt;</c> references.
        /// Statement failures are reported in the result rather than thrown; connection failures and cancellation throw.
        /// </summary>
        public static async Task<ScriptRunResult> RunAsync(ScriptRunOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);

            var files = ResolveFiles(options);
            var run = new RunContext(options, cancellationToken);
            var stopwatch = Stopwatch.StartNew();

            using (var executor = CreateExecutor(options.Provider))
            {
                executor.Open(options.ConnectionString);
                await run.ReportAsync(ScriptRunEventKind.ConnectionOpened, "Connection opened.");

                var visited = new HashSet<string>(ScriptHelpers.PathComparer);
                foreach (var file in files)
                {
                    if (run.Result.Stopped) break;
                    await ProcessPathAsync(file, executor, visited, run);
                }
            }

            run.Result.Elapsed = stopwatch.Elapsed;
            await run.ReportAsync(ScriptRunEventKind.RunCompleted, run.Result.Stopped ? "Run stopped." : "All selected scripts processed.");
            return run.Result;
        }

        /// <summary>
        /// Opens a connection and runs a block of SQL text (statements separated by lines containing only <c>GO</c>).
        /// Embedded file tags are resolved against <see cref="ScriptRunOptions.ScriptDirectory"/>, or the current directory.
        /// </summary>
        public static async Task<ScriptRunResult> RunSqlAsync(ScriptRunOptions options, string sql, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(sql);

            var run = new RunContext(options, cancellationToken);
            var stopwatch = Stopwatch.StartNew();

            using (var executor = CreateExecutor(options.Provider))
            {
                executor.Open(options.ConnectionString);
                await run.ReportAsync(ScriptRunEventKind.ConnectionOpened, "Connection opened.");
                await ExecuteStatementsAsync(sql, string.Empty, options.ScriptDirectory ?? Environment.CurrentDirectory, executor, run);
            }

            run.Result.Elapsed = stopwatch.Elapsed;
            await run.ReportAsync(ScriptRunEventKind.RunCompleted, run.Result.Stopped ? "Run stopped." : "SQL processed.");
            return run.Result;
        }

        /// <summary>
        /// Connects to a database and returns a script that recreates its schemas, tables, views, functions, procedures
        /// and triggers, optionally followed by INSERT statements for the existing data.
        /// </summary>
        public static string GenerateSchemaScript(DatabaseProvider provider, string connectionString, bool includeData = false)
        {
            using var executor = CreateExecutor(provider);
            executor.Open(connectionString);

            var script = new StringBuilder(executor.GenerateSchemaScript());
            if (includeData)
            {
                script.AppendLine();
                script.Append(executor.GenerateDataScript());
            }
            return script.ToString();
        }

        internal static IDbExecutor CreateExecutor(DatabaseProvider provider) => provider switch
        {
            DatabaseProvider.PostgreSQL => new PostgresExecutor(),
            DatabaseProvider.SqlServer => new SqlServerExecutor(),
            DatabaseProvider.Sqlite => new SqliteExecutor(),
            DatabaseProvider.MySQL => new MySqlExecutor(),
            DatabaseProvider.Oracle => new OracleExecutor(),
            DatabaseProvider.DB2 => CreateDb2Executor(),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider")
        };

        // Kept separate so the IBM.Data.Db2 assembly is only loaded when DB2 is actually used; hosts that never use
        // DB2 don't need to reference the IBM driver package.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IDbExecutor CreateDb2Executor() => new Db2Executor();

        private static List<string> ResolveFiles(ScriptRunOptions options)
        {
            if (options.Files.Count > 0)
                return options.Files.Select(f => ScriptHelpers.ResolvePath(f, options.ScriptDirectory ?? Environment.CurrentDirectory)).ToList();

            if (string.IsNullOrWhiteSpace(options.ScriptDirectory))
                throw new ArgumentException("Set ScriptDirectory, Files, or both.", nameof(options));
            if (!Directory.Exists(options.ScriptDirectory))
                throw new DirectoryNotFoundException($"Scripts directory not found: {options.ScriptDirectory}");

            return GetScriptFiles(options.ScriptDirectory).Select(Path.GetFullPath).ToList();
        }

        private static async Task ProcessPathAsync(string filePath, IDbExecutor executor, HashSet<string> visited, RunContext run)
        {
            run.CancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(filePath))
            {
                await run.ReportAsync(ScriptRunEventKind.Warning, $"File not found: {filePath}", filePath);
                return;
            }

            if (!visited.Add(filePath)) return;

            var ext = Path.GetExtension(filePath) ?? string.Empty;
            if (ext.Equals(".sql", StringComparison.OrdinalIgnoreCase))
            {
                await run.ReportAsync(ScriptRunEventKind.SqlFileStarted, $"Executing SQL file: {Path.GetFileName(filePath)}", filePath);
                run.Result.ExecutedFilesList.Add(filePath);

                var content = await File.ReadAllTextAsync(filePath, run.CancellationToken);
                await ExecuteStatementsAsync(content, filePath, Path.GetDirectoryName(filePath) ?? string.Empty, executor, run);
                return;
            }

            if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                await run.ReportAsync(ScriptRunEventKind.ListFileStarted, $"Processing list file: {Path.GetFileName(filePath)}", filePath);

                var lines = await File.ReadAllLinesAsync(filePath, run.CancellationToken);
                foreach (var raw in lines)
                {
                    if (run.Result.Stopped) return;
                    run.CancellationToken.ThrowIfCancellationRequested();
                    var line = raw.Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith(">>"))
                    {
                        var referenced = line.Substring(2).Trim();
                        var referencedFull = ScriptHelpers.ResolvePath(referenced, Path.GetDirectoryName(filePath));
                        await run.ReportAsync(ScriptRunEventKind.NestedListReference, $"Nested list reference: {referencedFull}", referencedFull);
                        await ProcessPathAsync(referencedFull, executor, visited, run);
                    }
                    else
                    {
                        var candidate = ScriptHelpers.ResolvePath(line, Path.GetDirectoryName(filePath));
                        var candidateExt = Path.GetExtension(candidate) ?? string.Empty;
                        if (candidateExt.Equals(".sql", StringComparison.OrdinalIgnoreCase) || candidateExt.Equals(".txt", StringComparison.OrdinalIgnoreCase) || File.Exists(candidate))
                        {
                            await ProcessPathAsync(candidate, executor, visited, run);
                        }
                        else
                        {
                            await run.ReportAsync(ScriptRunEventKind.Warning, $"Ignored line (not a file): {line} in {Path.GetFileName(filePath)}", filePath);
                        }
                    }
                }
                return;
            }

            await run.ReportAsync(ScriptRunEventKind.Warning, $"Unsupported file type: {filePath}", filePath);
        }

        private static async Task ExecuteStatementsAsync(string content, string filePath, string baseDir, IDbExecutor executor, RunContext run)
        {
            var statements = SqlHelpers.SplitStatements(content).ToList();
            var markers = run.Options.EmbeddedFileMarkers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fileName = Path.GetFileName(filePath);

            for (var i = 0; i < statements.Count; i++)
            {
                run.CancellationToken.ThrowIfCancellationRequested();
                var sql = statements[i];

                // Replace any embedded file tags in the statement (applies to countries inserts as well)
                try
                {
                    sql = ScriptHelpers.ReplaceEmbeddedTags(sql, baseDir, markers, run.Options.EnableEmbeddedTypeDetection);
                }
                catch (Exception ex)
                {
                    await run.ReportAsync(ScriptRunEventKind.Warning, $"Failed to replace embedded tags: {ex.Message}", filePath);
                    continue;
                }

                string label;
                string res;

                // Special handling: INSERT INTO Workflows with embedded file markers
                if (sql.IndexOf("INSERT INTO Workflows", StringComparison.OrdinalIgnoreCase) >= 0
                    && ScriptHelpers.TryExtractEmbeddedFileTag(sql, markers, out _, out _, out _, out _))
                {
                    // After replacement, the JSON content is already inlined and normalized, so parse and upsert as before
                    var parsed = ScriptHelpers.ParseWorkflowInsert(sql);
                    if (parsed == null)
                    {
                        await run.ReportAsync(ScriptRunEventKind.Warning, $"Unable to parse INSERT statement for workflow values in file {fileName}", filePath);
                        continue;
                    }

                    // Best-effort: the JSON definition spans the first '{' to the last '}'
                    var jsonStart = sql.IndexOf('{');
                    var jsonEnd = sql.LastIndexOf('}');
                    var jsonContent = jsonStart >= 0 && jsonEnd > jsonStart ? sql.Substring(jsonStart, jsonEnd - jsonStart + 1) : string.Empty;
                    if (string.IsNullOrWhiteSpace(jsonContent))
                    {
                        await run.ReportAsync(ScriptRunEventKind.Warning, $"Unable to extract JSON content for workflow '{parsed.Name}' in file {fileName}", filePath);
                        continue;
                    }

                    // validate and normalize json
                    try
                    {
                        using var doc = JsonDocument.Parse(jsonContent);
                        jsonContent = JsonSerializer.Serialize(doc.RootElement);
                    }
                    catch (Exception ex)
                    {
                        await run.ReportAsync(ScriptRunEventKind.Warning, $"Failed to parse JSON content for workflow '{parsed.Name}': {ex.Message}", filePath);
                        continue;
                    }

                    label = $"Upsert workflow '{parsed.Name}'";
                    res = executor.ExecuteUpsertWorkflow(parsed.Name, jsonContent, parsed.IsProduction, parsed.IsActive, parsed.IsValid,
                        parsed.CreatedAt ?? DateTime.UtcNow, parsed.UpdatedAt ?? DateTime.UtcNow, parsed.Version);
                }
                else
                {
                    label = $"Statement {i + 1}/{statements.Count}";
                    res = executor.ExecuteNonQuery(sql);
                }

                var result = new ScriptStatementResult(filePath, i + 1, statements.Count, sql, res == "Success" ? null : res);
                if (result.Succeeded)
                {
                    run.Result.StatementsSucceeded++;
                    await run.ReportAsync(ScriptRunEventKind.StatementSucceeded, $"OK {label}", filePath, result);
                    continue;
                }

                run.Result.FailuresList.Add(result);
                await run.ReportAsync(ScriptRunEventKind.StatementFailed, $"ERROR {label}: {res}", filePath, result);

                var action = run.Options.OnError?.Invoke(result) ?? run.Options.ErrorAction;
                if (action == ScriptErrorAction.Stop)
                {
                    run.Result.Stopped = true;
                    return;
                }
                if (action == ScriptErrorAction.SkipFile) return;
            }
        }

        // Per-run state shared by the recursive file walk
        private sealed class RunContext
        {
            public RunContext(ScriptRunOptions options, CancellationToken cancellationToken)
            {
                Options = options;
                CancellationToken = cancellationToken;
            }

            public ScriptRunOptions Options { get; }
            public CancellationToken CancellationToken { get; }
            public ScriptRunResult Result { get; } = new();

            public async Task ReportAsync(ScriptRunEventKind kind, string message, string? filePath = null, ScriptStatementResult? statement = null)
            {
                var evt = new ScriptRunEvent(kind, message, filePath, statement);
                Options.OnEvent?.Invoke(evt);
                if (Options.Log != null)
                    await Options.Log.WriteLineAsync($"[{evt.Timestamp:yyyy-MM-dd HH:mm:ss}] {message}");
            }
        }
    }
}
