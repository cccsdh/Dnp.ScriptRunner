/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using System.Text;
using Spectre.Console;

namespace Dnp.ScriptRunner.Screens
{
    // Generates a schema (and optionally data) create script for a connection and writes it to a chosen directory.
    static class SchemaScreen
    {
        public static int Show(string dbType, string connectionString, Settings settings)
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
                return ExitCodes.Success;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed to generate schema create script:[/] {Markup.Escape(ex.Message)}");
                return ExitCodes.SchemaFailed;
            }
        }
    }
}
