/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using Spectre.Console;

namespace Dnp.ScriptRunner
{
    // Non-interactive mode: builds a run request from command-line arguments without prompting.
    // Expected args: <dbType> <connectionString> <scriptDir> [file ...]
    static class CommandLineMode
    {
        public static bool Applies(string[] args) => args != null && args.Length >= 3;

        public static RequestOutcome Build(string[] args, Settings settings)
        {
            var connectionString = args[1];
            var scriptDir = args[2];

            if (!SqlScriptRunner.TryParseProvider(args[0], out var provider))
            {
                AnsiConsole.MarkupLine($"[red]Invalid database type '{Markup.Escape(args[0])}'. Allowed: {string.Join(", ", Enum.GetNames<DatabaseProvider>())}[/]");
                return RequestOutcome.Exit(ExitCodes.InvalidProvider);
            }
            var dbType = provider.ToString();

            if (!Directory.Exists(scriptDir))
            {
                AnsiConsole.MarkupLine($"[red]Scripts directory not found: {Markup.Escape(scriptDir)}[/]");
                return RequestOutcome.Exit(ExitCodes.DirectoryNotFound);
            }

            // persist provided connection/dir if not already present
            if (!settings.GetConnections(dbType).Contains(connectionString))
            {
                settings.AddConnection(dbType, connectionString);
                settings.Save();
            }

            if (!settings.GetDirectories(dbType).Contains(scriptDir))
            {
                settings.AddDirectory(dbType, scriptDir);
                settings.Save();
            }

            // Optional trailing arguments name specific files to run; otherwise run every script in the folder
            var selected = args.Length > 3
                ? args.Skip(3).ToList()
                : SqlScriptRunner.GetScriptFiles(scriptDir).Select(f => Path.GetFileName(f)).ToList();

            if (selected.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No .sql or .txt files found in directory.[/]");
                return RequestOutcome.Exit(ExitCodes.Success);
            }

            var files = selected.Select(f => ScriptHelpers.ResolvePath(f, scriptDir)).ToList();
            return RequestOutcome.Run(new RunRequest(provider, connectionString, scriptDir, files, Interactive: false));
        }
    }
}
