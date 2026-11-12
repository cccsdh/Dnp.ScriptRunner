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
    // Prompts for the database type and a connection string, offering saved connections for that provider.
    static class ConnectionScreen
    {
        private const string NewConnectionLabel = "<New connection...>";

        public static (string DbType, string ConnectionString) Show(Settings settings)
        {
            var dbType = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select database type")
                    .AddChoices(Enum.GetNames<DatabaseProvider>()));

            return (dbType, PromptForConnection(settings, dbType));
        }

        private static string PromptForConnection(Settings settings, string dbType)
        {
            var savedConns = settings.GetConnections(dbType);
            if (savedConns.Count > 0)
            {
                var connChoices = new List<string>(savedConns) { NewConnectionLabel };
                var pick = AnsiConsole.Prompt(
                    new SelectionPrompt<string>().Title("Select a saved connection or create new").AddChoices(connChoices));
                if (pick != NewConnectionLabel)
                    return pick;
            }

            // New connections are saved automatically
            var connectionString = PromptForConnectionString();
            settings.AddConnection(dbType, connectionString);
            settings.Save();
            return connectionString;
        }

        private static string PromptForConnectionString() =>
            AnsiConsole.Prompt(
                new TextPrompt<string>("Enter connection string")
                    .Validate(s => string.IsNullOrWhiteSpace(s) ? ValidationResult.Error("[red]Connection string required[/]") : ValidationResult.Success()));
    }
}
