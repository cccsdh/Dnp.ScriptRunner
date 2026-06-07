/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using Spectre.Console;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Dnp.ScriptRunner
{
    static class Helpers
    {
        // Update header: copyright symbol then year with no space and ensure it's printed once at top
        public static void RenderHeader()
        {
            var year = DateTime.Now.Year;
            var rule = new Rule($"[bold yellow]Dnp.ScriptRunner ©{year} Doughnuts Publishing[/]") { Justification = Justify.Center };
            AnsiConsole.Write(rule);
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
                var baseDir = Path.GetDirectoryName(filePath) ?? string.Empty;
                for (var i = 0; i < statements.Count; i++)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    var sql = statements[i];

                    // load markers from settings (configurable)
                    var settings = Settings.Load();
                    var markers = settings.EmbeddedFileMarkers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    // Replace any embedded file tags in the statement (applies to countries inserts as well)
                    try
                    {
                        sql = ScriptHelpers.ReplaceEmbeddedTags(sql, baseDir, markers, settings.EnableEmbeddedTypeDetection);
                    }
                    catch (Exception ex)
                    {
                        var err = $"Failed to replace embedded tags: {ex.Message}";
                        AnsiConsole.MarkupLine($"[red]{err}[/]");
                        await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {err}");
                        continue;
                    }

                    // Special handling: INSERT INTO Workflows with embedded file markers
                    if (sql.IndexOf("INSERT INTO Workflows", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (ScriptHelpers.TryExtractEmbeddedFileTag(sql, markers, out var openTag, out var closeTag, out var relativePath, out var fileType))
                        {
                            // After replacement, the JSON content is already inlined and normalized, so parse and upsert as before
                            var parsed = ScriptHelpers.ParseWorkflowInsert(sql);
                            if (parsed == null)
                            {
                                var err = $"Unable to parse INSERT statement for workflow values in file {Path.GetFileName(filePath)}";
                                AnsiConsole.MarkupLine($"[red]{err}[/]");
                                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {err}");
                                continue;
                            }

                            // Extract the JsonDefinition literal from the sql by finding the first single-quoted JSON value after Name
                            // Best-effort: find the first '{' after the Name occurrence
                            var jsonStart = sql.IndexOf('{');
                            var jsonEnd = sql.LastIndexOf('}');
                            string jsonContent = string.Empty;
                            if (jsonStart >= 0 && jsonEnd > jsonStart)
                            {
                                jsonContent = sql.Substring(jsonStart, jsonEnd - jsonStart + 1);
                            }

                            if (string.IsNullOrWhiteSpace(jsonContent))
                            {
                                var err = $"Unable to extract JSON content for workflow '{parsed.Name}' in file {Path.GetFileName(filePath)}";
                                AnsiConsole.MarkupLine($"[red]{err}[/]");
                                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {err}");
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
                                var err = $"Failed to parse JSON content for workflow '{parsed.Name}': {ex.Message}";
                                AnsiConsole.MarkupLine($"[red]{err}[/]");
                                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {err}");
                                continue;
                            }

                            var createdAt = parsed.CreatedAt ?? DateTime.UtcNow;
                            var updatedAt = parsed.UpdatedAt ?? DateTime.UtcNow;

                            var res = executor.ExecuteUpsertWorkflow(parsed.Name, jsonContent, parsed.IsProduction, parsed.IsActive, parsed.IsValid, createdAt, updatedAt, parsed.Version);
                            var resultMsg = res == "Success" ? $"OK Upsert workflow '{parsed.Name}'" : $"ERROR Upsert workflow '{parsed.Name}': {res}";
                            if (res == "Success") AnsiConsole.MarkupLine($" [green]{resultMsg}[/]"); else AnsiConsole.MarkupLine($" [red]{resultMsg}[/]");
                            await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {resultMsg}");

                            continue;
                        }
                    }

                    var res2 = executor.ExecuteNonQuery(sql);
                    var resultMsg2 = res2 == "Success"
                        ? $"OK Statement {i + 1}/{statements.Count}"
                        : $"ERROR Statement {i + 1}/{statements.Count}: {res2}";

                    if (res2 == "Success")
                        AnsiConsole.MarkupLine($" [green]{resultMsg2}[/]");
                    else
                        AnsiConsole.MarkupLine($" [red]{resultMsg2}[/]");

                    await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {resultMsg2}");

                    if (res2 != "Success")
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
}
