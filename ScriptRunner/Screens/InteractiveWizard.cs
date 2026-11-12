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
    // Interactive mode: walks the user through the connection, optional schema generation and script selection screens.
    static class InteractiveWizard
    {
        public static RequestOutcome Build(Settings settings)
        {
            var (dbType, connectionString) = ConnectionScreen.Show(settings);

            // Offer to generate a schema create script for this connection instead of running scripts
            if (AnsiConsole.Confirm("Generate a schema create script for this connection?", false))
            {
                return RequestOutcome.Exit(SchemaScreen.Show(dbType, connectionString, settings));
            }

            var selection = ScriptSelectionScreen.Show(settings, dbType);
            if (selection is null)
            {
                return RequestOutcome.Exit(ExitCodes.Success);
            }

            var (scriptDir, files) = selection.Value;
            var provider = Enum.Parse<DatabaseProvider>(dbType);
            return RequestOutcome.Run(new RunRequest(provider, connectionString, scriptDir, files, Interactive: true));
        }
    }
}
