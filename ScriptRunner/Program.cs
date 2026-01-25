/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;
using Spectre.Console;
using System.Threading.Channels;
using MySql.Data.MySqlClient;
using Oracle.ManagedDataAccess.Client;
using IBM.Data.DB2.Core;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace Dnp.ScriptRunner
{
    // Settings storage
    public class Settings
    {
        public Dictionary<string, List<string>> Connections { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Directories { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "script-runner-settings.json");

        public static Settings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new Settings();
                var json = File.ReadAllText(SettingsPath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<Settings>(json, opts) ?? new Settings();
            }
            catch
            {
                return new Settings();
            }
        }

        public void Save()
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(this, opts);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        public List<string> GetConnections(string provider)
        {
            if (Connections.TryGetValue(provider, out var list)) return list;
            return new List<string>();
        }

        public List<string> GetDirectories(string provider)
        {
            if (Directories.TryGetValue(provider, out var list)) return list;
            return new List<string>();
        }

        public void AddConnection(string provider, string connection)
        {
            if (!Connections.TryGetValue(provider, out var list))
            {
                list = new List<string>();
                Connections[provider] = list;
            }
            if (!list.Contains(connection)) list.Add(connection);
        }

        public void AddDirectory(string provider, string directory)
        {
            if (!Directories.TryGetValue(provider, out var list))
            {
                list = new List<string>();
                Directories[provider] = list;
            }
            if (!list.Contains(directory)) list.Add(directory);
        }
    }

    // Database executor abstraction
    interface IDbExecutor : IDisposable
    {
        void Open(string connectionString);
        string ExecuteNonQuery(string sql);
    }

    class SqlServerExecutor : IDbExecutor
    {
        private SqlConnection? _conn;
        public void Open(string connectionString) { _conn = new SqlConnection(connectionString); _conn.Open(); }
        public string ExecuteNonQuery(string sql)
        {
            using var cmd = _conn!.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 1800;
            try { cmd.ExecuteNonQuery(); return "Success"; } catch (Exception ex) { return ex.Message; }
        }
        public void Dispose() { try { _conn?.Close(); _conn?.Dispose(); } catch { } }
    }

    class PostgresExecutor : IDbExecutor
    {
        private NpgsqlConnection? _conn;
        public void Open(string connectionString) { _conn = new NpgsqlConnection(connectionString); _conn.Open(); }
        public string ExecuteNonQuery(string sql)
        {
            using var cmd = _conn!.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 1800;
            try { cmd.ExecuteNonQuery(); return "Success"; } catch (Exception ex) { return ex.Message; }
        }
        public void Dispose() { try { _conn?.Close(); _conn?.Dispose(); } catch { } }
    }

    class SqliteExecutor : IDbExecutor
    {
        private SqliteConnection? _conn;

        public void Open(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("connectionString is required", nameof(connectionString));

            // If the input looks like a raw file path (no '='), build a proper connection string
            if (!connectionString.Contains('='))
            {
                var builder = new SqliteConnectionStringBuilder { DataSource = connectionString };
                connectionString = builder.ToString();
            }

            _conn = new SqliteConnection(connectionString);
            _conn.Open();
        }

        public string ExecuteNonQuery(string sql)
        {
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 1800;
            try { cmd.ExecuteNonQuery(); return "Success"; } catch (Exception ex) { return ex.Message; }
        }

        public void Dispose() { try { _conn?.Close(); _conn?.Dispose(); } catch { } }
    }

    class MySqlExecutor : IDbExecutor
    {
        private MySqlConnection? _conn;
        public void Open(string connectionString) { _conn = new MySqlConnection(connectionString); _conn.Open(); }
        public string ExecuteNonQuery(string sql)
        {
            using var cmd = _conn!.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 1800;
            try { cmd.ExecuteNonQuery(); return "Success"; } catch (Exception ex) { return ex.Message; }
        }
        public void Dispose() { try { _conn?.Close(); _conn?.Dispose(); } catch { } }
    }

    class OracleExecutor : IDbExecutor
    {
        private OracleConnection? _conn;
        public void Open(string connectionString) { _conn = new OracleConnection(connectionString); _conn.Open(); }
        public string ExecuteNonQuery(string sql)
        {
            using var cmd = _conn!.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 1800;
            try { cmd.ExecuteNonQuery(); return "Success"; } catch (Exception ex) { return ex.Message; }
        }
        public void Dispose() { try { _conn?.Close(); _conn?.Dispose(); } catch { } }
    }

    class Db2Executor : IDbExecutor
    {
        private DB2Connection? _conn;
        public void Open(string connectionString) { _conn = new DB2Connection(connectionString); _conn.Open(); }
        public string ExecuteNonQuery(string sql)
        {
            using var cmd = _conn!.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 1800;
            try { cmd.ExecuteNonQuery(); return "Success"; } catch (Exception ex) { return ex.Message; }
        }
        public void Dispose() { try { _conn?.Close(); _conn?.Dispose(); } catch { } }
    }

    static class Helpers
    {
        // Update header: copyright symbol then year with no space and ensure it's printed once at top
        public static void RenderHeader()
        {
            var year = DateTime.Now.Year;
            var rule = new Spectre.Console.Rule($"[bold yellow]Dnp.ScriptRunner ©{year} Doughnuts Publishing[/]") { Alignment = Spectre.Console.Justify.Center };
            AnsiConsole.Render(rule);
            // add a separator line so subsequent output appears below the header
            AnsiConsole.WriteLine();
        }

        public static IEnumerable<string> SplitStatements(string sql)
        {
            var sb = new StringBuilder();
            using var reader = new StringReader(sql);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
                {
                    var s = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(s)) yield return s;
                    sb.Clear();
                }
                else
                {
                    sb.AppendLine(line);
                }
            }

            var last = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(last)) yield return last;
        }

        public static List<string> ExpandFiles(IEnumerable<string> selectedFiles, string baseDirectory)
        {
            var result = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in selectedFiles)
            {
                var full = Path.GetFullPath(Path.Combine(baseDirectory, f));
                ExpandRecursive(full, result, visited);
            }
            return result;
        }

        public static void ExpandRecursive(string filePath, List<string> output, HashSet<string> visited)
        {
            if (!File.Exists(filePath)) return;
            if (visited.Contains(filePath)) return;
            visited.Add(filePath);
            if (filePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                output.Add(filePath);
                return;
            }

            if (filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    if (trimmed.StartsWith(">>"))
                    {
                        var referenced = trimmed.Substring(2).Trim();
                        var referencedFull = Path.IsPathRooted(referenced)
                            ? referenced
                            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, referenced));
                        ExpandRecursive(referencedFull, output, visited);
                    }
                    else
                    {
                        var candidate = Path.IsPathRooted(trimmed)
                            ? trimmed
                            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, trimmed));
                        if (File.Exists(candidate) && candidate.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                            output.Add(candidate);
                    }
                }
            }
        }

        // Process files and write progress to provided TextWriter
        public static async Task ProcessPathAsync(string filePath, IDbExecutor executor, HashSet<string> visited, CancellationToken ct, TextWriter writer)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

            if (!File.Exists(filePath))
            {
                var msg = $"File not found: {filePath}";
                AnsiConsole.MarkupLine($"[yellow]{msg}[/]");
                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}");
                return;
            }

            if (visited.Contains(filePath)) return;
            visited.Add(filePath);

            var ext = Path.GetExtension(filePath) ?? string.Empty;
            if (ext.Equals(".sql", StringComparison.OrdinalIgnoreCase))
            {
                var headerMsg = $"Executing SQL file: {Path.GetFileName(filePath)}";
                AnsiConsole.MarkupLine($"[green]{headerMsg}[/]");
                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {headerMsg}");

                var content = await File.ReadAllTextAsync(filePath, ct);
                var statements = SplitStatements(content).ToList();
                for (var i = 0; i < statements.Count; i++)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    var sql = statements[i];
                    var res = executor.ExecuteNonQuery(sql);
                    var resultMsg = res == "Success"
                        ? $"OK Statement {i + 1}/{statements.Count}"
                        : $"ERROR Statement {i + 1}/{statements.Count}: {res}";

                    if (res == "Success")
                        AnsiConsole.MarkupLine($" [green]{resultMsg}[/]");
                    else
                        AnsiConsole.MarkupLine($" [red]{resultMsg}[/]");

                    await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {resultMsg}");

                    if (res != "Success")
                    {
                        var action = AnsiConsole.Prompt(new SelectionPrompt<string>().AddChoices(new[] { "Continue", "Skip File", "Stop" }));
                        if (action == "Stop") ct.ThrowIfCancellationRequested();
                        if (action == "Skip File") break;
                    }
                }
                return;
            }

            if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var headerMsg = $"Processing list file: {Path.GetFileName(filePath)}";
                AnsiConsole.MarkupLine($"[green]{headerMsg}[/]");
                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {headerMsg}");

                var lines = await File.ReadAllLinesAsync(filePath, ct);
                foreach (var raw in lines)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    var line = raw.Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith(">>"))
                    {
                        var referenced = line.Substring(2).Trim();
                        var referencedFull = Path.IsPathRooted(referenced)
                            ? referenced
                            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, referenced));
                        await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Nested list reference: {referencedFull}");
                        await ProcessPathAsync(referencedFull, executor, visited, ct, writer);
                    }
                    else
                    {
                        var candidate = Path.IsPathRooted(line)
                            ? line
                            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, line));

                        var candidateExt = Path.GetExtension(candidate) ?? string.Empty;
                        if (candidateExt.Equals(".sql", StringComparison.OrdinalIgnoreCase) || candidateExt.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                        {
                            await ProcessPathAsync(candidate, executor, visited, ct, writer);
                        }
                        else
                        {
                            if (File.Exists(candidate)) await ProcessPathAsync(candidate, executor, visited, ct, writer);
                            else
                            {
                                var ignored = $"Ignored line (not a file): {line} in {Path.GetFileName(filePath)}";
                                AnsiConsole.MarkupLine($"[yellow]{ignored}[/]");
                                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ignored}");
                            }
                        }
                    }
                }
                return;
            }

            var unsupported = $"Unsupported file type: {filePath}";
            AnsiConsole.MarkupLine($"[yellow]{unsupported}[/]");
            await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {unsupported}");
        }
    }

    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Helpers.RenderHeader();

            var settings = Settings.Load();

            var dbType = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select database type")
                    .AddChoices(new[] { "PostgreSQL", "SqlServer", "Sqlite", "MySQL", "Oracle", "DB2" }));

            // Connection string: offer saved connections per provider
            string connectionString;
            var savedConns = settings.GetConnections(dbType);
            if (savedConns.Count > 0)
            {
                var connChoices = new List<string>(savedConns);
                connChoices.Add("<New connection...>");
                var pick = AnsiConsole.Prompt(
                    new SelectionPrompt<string>().Title("Select a saved connection or create new").AddChoices(connChoices));
                if (pick == "<New connection...>")
                {
                    connectionString = AnsiConsole.Prompt(
                        new TextPrompt<string>("Enter connection string")
                            .Validate(s => string.IsNullOrWhiteSpace(s) ? ValidationResult.Error("[red]Connection string required[/]") : ValidationResult.Success()));
                    // Auto-save new connection
                    settings.AddConnection(dbType, connectionString);
                    settings.Save();
                }
                else
                {
                    connectionString = pick;
                }
            }
            else
            {
                connectionString = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter connection string")
                        .Validate(s => string.IsNullOrWhiteSpace(s) ? ValidationResult.Error("[red]Connection string required[/]") : ValidationResult.Success()));
                // Auto-save when no saved connections exist
                settings.AddConnection(dbType, connectionString);
                settings.Save();
            }

            // Script directory: offer saved directories per provider
            string scriptDir;
            var savedDirs = settings.GetDirectories(dbType);
            if (savedDirs.Count > 0)
            {
                var dirChoices = new List<string>(savedDirs);
                dirChoices.Add("<New directory...>");
                var pickDir = AnsiConsole.Prompt(
                    new SelectionPrompt<string>().Title("Select a saved scripts directory or create new").AddChoices(dirChoices));
                if (pickDir == "<New directory...>")
                {
                    scriptDir = AnsiConsole.Prompt(
                        new TextPrompt<string>("Scripts directory (full path)")
                            .DefaultValue(Environment.CurrentDirectory)
                            .Validate(s => System.IO.Directory.Exists(s) ? ValidationResult.Success() : ValidationResult.Error("[red]Directory not found[/]")));
                    // Auto-save new directory
                    settings.AddDirectory(dbType, scriptDir);
                    settings.Save();
                }
                else
                {
                    scriptDir = pickDir;
                }
            }
            else
            {
                scriptDir = AnsiConsole.Prompt(
                    new TextPrompt<string>("Scripts directory (full path)")
                        .DefaultValue(Environment.CurrentDirectory)
                        .Validate(s => System.IO.Directory.Exists(s) ? ValidationResult.Success() : ValidationResult.Error("[red]Directory not found[/]")));
                // Auto-save when no saved directories exist
                settings.AddDirectory(dbType, scriptDir);
                settings.Save();
            }

            var files = System.IO.Directory.EnumerateFiles(scriptDir, "*.*", System.IO.SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .Select(f => System.IO.Path.GetFileName(f))
                .OrderBy(n => n)
                .ToList();

            if (!files.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No .sql or .txt files found in directory.[/]");
                return 0;
            }

            var selected = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Select files to include (use [green]<space>[/] to toggle). You can select .sql and .txt")
                    .PageSize(10)
                    .AddChoices(files));

            if (selected == null || selected.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No files selected. Exiting.[/]");
                return 0;
            }

            // Setup agent channel
            var channel = Channel.CreateUnbounded<Func<Task>>();
            var cts = new CancellationTokenSource();

            // Start agent runner
            var agent = Task.Run(async () =>
            {
                await foreach (var work in channel.Reader.ReadAllAsync(cts.Token))
                {
                    try
                    {
                        await work();
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Agent work exception:[/] {ex.Message}");
                    }
                }
            }, cts.Token);

            IDbExecutor executor = dbType switch
            {
                "PostgreSQL" => new PostgresExecutor(),
                "SqlServer" => new SqlServerExecutor(),
                "Sqlite" => new SqliteExecutor(),
                "MySQL" => new MySqlExecutor(),
                "Oracle" => new OracleExecutor(),
                "DB2" => new Db2Executor(),
                _ => throw new InvalidOperationException("Unsupported database")
            };

            try
            {
                AnsiConsole.Status().Start("Opening connection...", ctx => executor.Open(connectionString));
                AnsiConsole.MarkupLine("[green]Connection opened.[/]");

                // Preserve choice order (files list order) when queuing selected items
                var selectedSet = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
                var orderedSelectedFullPaths = files
                    .Where(f => selectedSet.Contains(f))
                    .Select(f => Path.GetFullPath(Path.Combine(scriptDir, f)))
                    .ToList();

                var visitedGlobal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var total = orderedSelectedFullPaths.Count;
                var index = 0;

                // Create single output log file for the run
                var outputFile = Path.Combine(scriptDir, $"script-run-results-{DateTime.Now:yyyyMMdd_HHmmss}.log");
                using var fileWriter = new StreamWriter(outputFile, false, Encoding.UTF8) { AutoFlush = true };

                // Queue one work item per top-level selected file. The work item will process the file lines in order and recurse.
                foreach (var topFile in orderedSelectedFullPaths)
                {
                    index++;
                    var topFileName = Path.GetFileName(topFile);
                    var queuedMsg = $"({index}/{total}) Queuing {topFileName}";
                    AnsiConsole.MarkupLine($"[blue]{queuedMsg}[/]");
                    await fileWriter.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {queuedMsg}");

                    await channel.Writer.WriteAsync(async () =>
                    {
                        await Helpers.ProcessPathAsync(topFile, executor, visitedGlobal, cts.Token, fileWriter);
                    }, cts.Token);
                }

                // mark completion
                channel.Writer.Complete();
                await agent;

                // write completion note and close writer
                await fileWriter.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] All selected scripts processed.");
                fileWriter.Close();

                Console.Clear();
                Helpers.RenderHeader();
                AnsiConsole.MarkupLine("[green]All selected scripts processed.[/]");

                var json = JsonSerializer.Serialize(orderedSelectedFullPaths);
                File.WriteAllText("output.json", json);
                AnsiConsole.MarkupLine($"[green]Run log written to[/] {outputFile}");

                if (AnsiConsole.Confirm("Do you want to open the run results now?"))
                {
                    try
                    {
                        var psi = new ProcessStartInfo(outputFile) { UseShellExecute = true };
                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Unable to open file:[/] {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Execution cancelled.[/]");

                var cancellationInfo = new
                {
                    Timestamp = DateTime.UtcNow,
                    Status = "Cancelled",
                    ProcessedFiles = files.Count,
                    RemainingFiles = 0
                };

                var json = JsonSerializer.Serialize(cancellationInfo);
                File.WriteAllText("cancellation_info.json", json);
                AnsiConsole.MarkupLine("[green]Cancellation information written to cancellation_info.json[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Fatal error:[/] {ex.Message}");

                var errorInfo = new
                {
                    Timestamp = DateTime.UtcNow,
                    Status = "Error",
                    Message = ex.Message,
                    StackTrace = ex.StackTrace
                };

                var json = JsonSerializer.Serialize(errorInfo);
                File.WriteAllText("error_info.json", json);
                AnsiConsole.MarkupLine("[green]Error information written to error_info.json[/]");
            }
            finally
            {
                executor.Dispose();
            }

            return 0;
        }
    }
}
