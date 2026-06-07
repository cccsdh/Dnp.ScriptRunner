/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using System;
using System.Linq;

namespace Dnp.ScriptRunner
{
    // Database executor abstraction
    interface IDbExecutor : IDisposable
    {
        void Open(string connectionString);
        string ExecuteNonQuery(string sql);
        // Upsert helper for workflows JSON definitions
        string ExecuteUpsertWorkflow(string name, string jsonDefinition, int isProduction, int isActive, int isValid, DateTime createdAt, DateTime updatedAt, int version);
    }
}
