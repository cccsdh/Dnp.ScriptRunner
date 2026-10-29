/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using System.Text;
using System.Text.Json;
using Spectre.Console;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace Dnp.ScriptRunner
{

    // Application entrypoint. The script engine itself lives in Dnp.ScriptRunner.Core (SqlScriptRunner), so other
    // programs can run the same scripts by referencing that library instead of shelling out to this executable.
    class Program
    {
        // Exit codes returned to calling processes / CI pipelines
        const int ExitSuccess = 0;
        const int ExitScriptErrors = 1;
        const int ExitInvalidProvider = 2;
        const int ExitDirectoryNotFound = 3;
        const int ExitSchemaFailed = 4;
        const int ExitFatal = 5;

        static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Helpers.RenderHeader();

            var settings = Settings.Load();

            // CLI mode: if args provided use them to bypass interactive prompts
            // Expected args: <dbType> <connectionString> <scriptDir> [file ...]
            string dbType;
            string connectionString;
            string scriptDir;
            List<string> selected;
            var allowedDbTypes = Enum.GetNames<DatabaseProvider>();
            var nonInteractive = args != null && args.Length >= 3;
            if (nonInteractive)
            {
                dbType = args![0];
                connectionString = args[1];
                scriptDir = args[2];

                if (!SqlScriptRunner.TryParseProvider(dbType, out var parsed))
                {
                    AnsiConsole.MarkupLine($"[red]Invalid database type '{Markup.Escape(dbType)}'. Allowed: {string.Join(", ", allowedDbTypes)}[/]");
                    return ExitInvalidProvider;
                }
                dbType = parsed.ToString();

                if (!Directory.Exists(scriptDir))
                {
                    AnsiConsole.MarkupLine($"[red]Scripts directory not found: {Markup.Escape(scriptDir)}[/]");
                    return ExitDirectoryNotFound;
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

                // Optional trailing arguments name specific files to run; otherwise run every script in the folder
                selected = args.Length > 3
                    ? args.Skip(3).ToList()
                    : SqlScriptRunner.GetScriptFiles(scriptDir).Select(f => Path.GetFileName(f)).ToList();

                if (selected.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No .sql or .txt files found in directory.[/]");
                    return ExitSuccess;
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

                // Offer to generate a schema create script for this connection instead of running scripts
                if (AnsiConsole.Confirm("Generate a schema create script for this connection?", false))
                {
                    return GenerateSchemaScript(dbType, connectionString, settings);
                }

                // Script directory: offer saved directories per provider
                scriptDir = Helpers.PromptForDirectory(settings, dbType, "Select a saved scripts directory or create new", "Scripts directory (full path)");

                var files = SqlScriptRunner.GetScriptFiles(scriptDir).Select(f => Path.GetFileName(f)).ToList();
                if (!files.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]No .sql or .txt files found in directory.[/]");
                    return ExitSuccess;
                }

                selected = AnsiConsole.Prompt(
                    new MultiSelectionPrompt<string>()
                        .Title("Select files to include (use [green]<space>[/] to toggle). You can select .sql and .txt")
                        .PageSize(10)
                        .AddChoices(files));

                if (selected == null || selected.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No files selected. Exiting.[/]");
                    return ExitSuccess;
                }

                // Preserve folder order regardless of the order items were toggled
                var selectedSet = new HashSet<string>(selected, ScriptHelpers.PathComparer);
                selected = files.Where(f => selectedSet.Contains(f)).ToList();
            }

            var provider = Enum.Parse<DatabaseProvider>(dbType);
            var orderedSelectedFullPaths = selected.Select(f => ScriptHelpers.ResolvePath(f, scriptDir)).ToList();

            // Ctrl+C cancels the run cleanly instead of killing the process mid-statement
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            try
            {
                // Create single output log file for the run
                var outputFile = Path.Combine(scriptDir, $"script-run-results-{DateTime.Now:yyyyMMdd_HHmmss}.log");
                using var fileWriter = new StreamWriter(outputFile, false, Encoding.UTF8) { AutoFlush = true };

                for (var i = 0; i < orderedSelectedFullPaths.Count; i++)
                {
                    var queuedMsg = $"({i + 1}/{orderedSelectedFullPaths.Count}) Queuing {Path.GetFileName(orderedSelectedFullPaths[i])}";
                    AnsiConsole.MarkupLine($"[blue]{Markup.Escape(queuedMsg)}[/]");
                    await fileWriter.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {queuedMsg}");
                }

                var options = new ScriptRunOptions(provider, connectionString)
                {
                    ScriptDirectory = scriptDir,
                    Files = orderedSelectedFullPaths,
                    Log = fileWriter,
                    EmbeddedFileMarkers = settings.EmbeddedFileMarkers,
                    EnableEmbeddedTypeDetection = settings.EnableEmbeddedTypeDetection,
                    OnEvent = WriteEvent,
                    // CLI runs must never block on a prompt: stop at the first failure and report it via the exit code
                    ErrorAction = ScriptErrorAction.Stop,
                    OnError = nonInteractive ? null : PromptForErrorAction
                };

                AnsiConsole.MarkupLine("[grey]Opening connection...[/]");
                var result = await SqlScriptRunner.RunAsync(options, cts.Token);
                fileWriter.Close();

                if (!nonInteractive)
                {
                    Console.Clear();
                    Helpers.RenderHeader();
                }

                var summary = $"{result.StatementsSucceeded} statement(s) succeeded, {result.StatementsFailed} failed in {result.Elapsed.TotalSeconds:0.0}s.";
                AnsiConsole.MarkupLine(result.Success
                    ? $"[green]All selected scripts processed.[/] {summary}"
                    : $"[red]{(result.Stopped ? "Run stopped after an error." : "Run completed with errors.")}[/] {summary}");

                var json = JsonSerializer.Serialize(orderedSelectedFullPaths);
                File.WriteAllText("output.json", json);
                AnsiConsole.MarkupLine($"[green]Run log written to[/] {Markup.Escape(outputFile)}");

                if (!nonInteractive && AnsiConsole.Confirm("Do you want to open the run results now?"))
                {
                    try
                    {
                        var psi = new ProcessStartInfo(outputFile) { UseShellExecute = true };
                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Unable to open file:[/] {Markup.Escape(ex.Message)}");
                    }
                }

                return result.Success ? ExitSuccess : ExitScriptErrors;
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Execution cancelled.[/]");

                var cancellationInfo = new
                {
                    Timestamp = DateTime.UtcNow,
                    Status = "Cancelled",
                    SelectedFiles = orderedSelectedFullPaths.Count
                };

                var json = JsonSerializer.Serialize(cancellationInfo);
                File.WriteAllText("cancellation_info.json", json);
                AnsiConsole.MarkupLine("[green]Cancellation information written to cancellation_info.json[/]");
                return ExitScriptErrors;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Fatal error:[/] {Markup.Escape(ex.Message)}");

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
                return ExitFatal;
            }
        }

        // Interactive mode asks what to do after each failed statement
        private static ScriptErrorAction PromptForErrorAction(ScriptStatementResult failed)
        {
            var action = AnsiConsole.Prompt(new SelectionPrompt<string>().AddChoices("Continue", "Skip File", "Stop"));
            return action switch
            {
                "Continue" => ScriptErrorAction.Continue,
                "Skip File" => ScriptErrorAction.SkipFile,
                _ => ScriptErrorAction.Stop
            };
        }

        // Renders engine progress events to the console
        private static void WriteEvent(ScriptRunEvent e)
        {
            var text = Markup.Escape(e.Message);
            switch (e.Kind)
            {
                case ScriptRunEventKind.ConnectionOpened:
                case ScriptRunEventKind.SqlFileStarted:
                case ScriptRunEventKind.ListFileStarted:
                    AnsiConsole.MarkupLine($"[green]{text}[/]");
                    break;
                case ScriptRunEventKind.StatementSucceeded:
                    AnsiConsole.MarkupLine($" [green]{text}[/]");
                    break;
                case ScriptRunEventKind.StatementFailed:
                    AnsiConsole.MarkupLine($" [red]{text}[/]");
                    break;
                case ScriptRunEventKind.Warning:
                    AnsiConsole.MarkupLine($"[yellow]{text}[/]");
                    break;
            }
        }

        private static int GenerateSchemaScript(string dbType, string connectionString, Settings settings)
        {
            var includeData = AnsiConsole.Confirm("Include data (INSERT statements for each table) in the script as well?", false);

            var outputDir = Helpers.PromptForDirectory(settings, dbType, "Select a saved directory to save the schema script or create new", "Output directory for schema script (full path)", createIfMissing: true);

            try
            {
                var script = string.Empty;
                AnsiConsole.Status().Start(includeData ? "Generating schema and data script..." : "Generating schema create script...",
                    ctx => script = SqlScriptRunner.GenerateSchemaScript(Enum.Parse<DatabaseProvider>(dbType), connectionString, includeData));

                var outputFile = Path.Combine(outputDir, $"schema-create-{dbType}-{DateTime.Now:yyyyMMdd_HHmmss}.sql");
                File.WriteAllText(outputFile, script, Encoding.UTF8);

                AnsiConsole.MarkupLine($"[green]Schema create script written to[/] {Markup.Escape(outputFile)}");
                return ExitSuccess;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to generate schema create script:[/] {Markup.Escape(ex.Message)}");
                return ExitSchemaFailed;
            }
        }
    }
}
