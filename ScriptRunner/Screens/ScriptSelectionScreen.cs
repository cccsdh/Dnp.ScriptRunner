/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using Spectre.Console;

namespace Dnp.ScriptRunner.Screens
{
    // Prompts for a scripts directory and the files within it to run.
    static class ScriptSelectionScreen
    {
        // Returns the chosen directory and the selected files as full paths in folder order, or null when there is nothing to run.
        public static (string ScriptDir, List<string> Files)? Show(Settings settings, string dbType)
        {
            var scriptDir = Helpers.PromptForDirectory(settings, dbType, "Select a saved scripts directory or create new", "Scripts directory (full path)");

            var files = SqlScriptRunner.GetScriptFiles(scriptDir).Select(f => Path.GetFileName(f)).ToList();
            if (files.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No .sql or .txt files found in directory.[/]");
                return null;
            }

            var selected = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Select files to include (use [green]<space>[/] to toggle). You can select .sql and .txt")
                    .PageSize(10)
                    .AddChoices(files));

            if (selected == null || selected.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No files selected. Exiting.[/]");
                return null;
            }

            // Preserve folder order regardless of the order items were toggled
            var selectedSet = new HashSet<string>(selected, ScriptHelpers.PathComparer);
            var ordered = files
                .Where(f => selectedSet.Contains(f))
                .Select(f => ScriptHelpers.ResolvePath(f, scriptDir))
                .ToList();

            return (scriptDir, ordered);
        }
    }
}
