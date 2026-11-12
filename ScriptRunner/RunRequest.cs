/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

namespace Dnp.ScriptRunner
{
    // Everything needed to execute a run, gathered either from the command line or the interactive screens.
    // Files are full paths, already in the order they should run.
    record RunRequest(DatabaseProvider Provider, string ConnectionString, string ScriptDir, List<string> Files, bool Interactive);

    // Result of an input step: either a request to run, or an exit code to return immediately
    // (validation failure, nothing selected, or a different action such as schema generation was taken instead).
    readonly record struct RequestOutcome(RunRequest? Request, int ExitCode)
    {
        public static RequestOutcome Run(RunRequest request) => new(request, ExitCodes.Success);
        public static RequestOutcome Exit(int exitCode) => new(null, exitCode);
    }
}
