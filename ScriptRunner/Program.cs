/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using System.Text;
using Dnp.ScriptRunner.Screens;

namespace Dnp.ScriptRunner
{
    // Application entrypoint. The script engine itself lives in Dnp.ScriptRunner.Core (SqlScriptRunner), so other
    // programs can run the same scripts by referencing that library instead of shelling out to this executable.
    //
    // Flow: build a RunRequest (from CLI args, or the interactive screens under Screens/), then hand it to RunScreen.
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Helpers.RenderHeader();

            var settings = Settings.Load();

            var outcome = CommandLineMode.Applies(args)
                ? CommandLineMode.Build(args, settings)
                : InteractiveWizard.Build(settings);

            if (outcome.Request is null)
                return outcome.ExitCode;

            return await RunScreen.RunAsync(outcome.Request, settings);
        }
    }
}
