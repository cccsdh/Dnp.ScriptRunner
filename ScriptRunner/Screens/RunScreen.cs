/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace Dnp.ScriptRunner.Screens
{
    // Executes a run request: shows live progress, handles errors/cancellation, and reports the summary.
    static class RunScreen
    {
        public static async Task<int> RunAsync(RunRequest request, Settings settings)
        {
            var files = request.Files;

            // Ctrl+C cancels the run cleanly instead of killing the process mid-statement
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            try
            {
                // Create single output log file for the run
                var outputFile = Path.Combine(request.ScriptDir, $"script-run-results-{DateTime.Now:yyyyMMdd_HHmmss}.log");
                using var fileWriter = new StreamWriter(outputFile, false, Encoding.UTF8) { AutoFlush = true };

                for (var i = 0; i < files.Count; i++)
                {
                    var queuedMsg = $"({i + 1}/{files.Count}) Queuing {Path.GetFileName(files[i])}";
                    AnsiConsole.MarkupLine($"[blue]{Markup.Escape(queuedMsg)}[/]");
                    await fileWriter.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {queuedMsg}");
                }

                var options = new ScriptRunOptions(request.Provider, request.ConnectionString)
                {
                    ScriptDirectory = request.ScriptDir,
                    Files = files,
                    Log = fileWriter,
                    EmbeddedFileMarkers = settings.EmbeddedFileMarkers,
                    EnableEmbeddedTypeDetection = settings.EnableEmbeddedTypeDetection,
                    OnEvent = WriteEvent,
                    // CLI runs must never block on a prompt: stop at the first failure and report it via the exit code
                    ErrorAction = ScriptErrorAction.Stop,
                    OnError = request.Interactive ? PromptForErrorAction : null
                };

                AnsiConsole.MarkupLine("[grey]Opening connection...[/]");
                var result = await SqlScriptRunner.RunAsync(options, cts.Token);
                fileWriter.Close();

                if (request.Interactive)
                {
                    Console.Clear();
                    Helpers.RenderHeader();
                }

                var summary = $"{result.StatementsSucceeded} statement(s) succeeded, {result.StatementsFailed} failed in {result.Elapsed.TotalSeconds:0.0}s.";
                AnsiConsole.MarkupLine(result.Success
                    ? $"[green]All selected scripts processed.[/] {summary}"
                    : $"[red]{(result.Stopped ? "Run stopped after an error." : "Run completed with errors.")}[/] {summary}");

                var json = JsonSerializer.Serialize(files);
                File.WriteAllText("output.json", json);
                AnsiConsole.MarkupLine($"[green]Run log written to[/] {Markup.Escape(outputFile)}");

                if (request.Interactive && AnsiConsole.Confirm("Do you want to open the run results now?"))
                {
                    OpenFile(outputFile);
                }

                return result.Success ? ExitCodes.Success : ExitCodes.ScriptErrors;
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Execution cancelled.[/]");

                var cancellationInfo = new
                {
                    Timestamp = DateTime.UtcNow,
                    Status = "Cancelled",
                    SelectedFiles = files.Count
                };

                File.WriteAllText("cancellation_info.json", JsonSerializer.Serialize(cancellationInfo));
                AnsiConsole.MarkupLine("[green]Cancellation information written to cancellation_info.json[/]");
                return ExitCodes.ScriptErrors;
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

                File.WriteAllText("error_info.json", JsonSerializer.Serialize(errorInfo));
                AnsiConsole.MarkupLine("[green]Error information written to error_info.json[/]");
                return ExitCodes.Fatal;
            }
        }

        private static void OpenFile(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Unable to open file:[/] {Markup.Escape(ex.Message)}");
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
    }
}
