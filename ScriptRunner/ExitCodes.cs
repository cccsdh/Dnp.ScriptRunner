/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

namespace Dnp.ScriptRunner
{
    // Exit codes returned to calling processes / CI pipelines
    static class ExitCodes
    {
        public const int Success = 0;
        public const int ScriptErrors = 1;
        public const int InvalidProvider = 2;
        public const int DirectoryNotFound = 3;
        public const int SchemaFailed = 4;
        public const int Fatal = 5;
    }
}
