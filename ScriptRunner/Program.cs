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
using System.Data.Common;
using System.Xml;

namespace Dnp.ScriptRunner
{

    // Application entrypoint
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Helpers.RenderHeader();

            var settings = Settings.Load();

            // CLI mode: if args provided use them to bypass interactive prompts
            // Expected args: <dbType> <connectionString> <scriptDir>
            string dbType;
            string connectionString;
            string scriptDir;
            var allowedDbTypes = new[] { "PostgreSQL", "SqlServer", "Sqlite", "MySQL", "Oracle", "DB2" };
            var nonInteractive = args != null && args.Length >= 3;
            if (nonInteractive)
            {
                dbType = args[0];
                connectionString = args[1];
                scriptDir = args[2];

                if (!allowedDbTypes.Contains(dbType))
                {
                    AnsiConsole.MarkupLine($"[red]Invalid database type '{dbType}'. Allowed: {string.Join(", ", allowedDbTypes)}[/]");
                    return 2;
                }

                if (!Directory.Exists(scriptDir))
                {
                    AnsiConsole.MarkupLine($"[red]Scripts directory not found: {scriptDir}[/]");
                    return 3;
                }

                // persist provided connection/dir if not already present
                var savedConns = settings.GetConnections(dbType);
                if (!savedConns.Contains(connectionString))
                {
                    settings.AddConnection(dbType, connectionString);
                    settings.Save();
                }

                var savedDirs = settings.GetDirectories(dbType);
                if (!savedDirs.Contains(scriptDir))
                {
                    settings.AddDirectory(dbType, scriptDir);
                    settings.Save();
                }
            }
            else
            {
                dbType = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("Select database type")
                        .AddChoices(allowedDbTypes));

                // Connection string: offer saved connections per provider
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
