/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using System;
using System.Collections.Generic;
using System.IO;

namespace Dnp.ScriptRunner
{
    /// <summary>The database engines ScriptRunner can connect to.</summary>
    public enum DatabaseProvider
    {
        PostgreSQL,
        SqlServer,
        Sqlite,
        MySQL,
        Oracle,
        DB2
    }

    /// <summary>What to do after a statement fails.</summary>
    public enum ScriptErrorAction
    {
        /// <summary>Carry on with the next statement.</summary>
        Continue,
        /// <summary>Skip the remaining statements in the current .sql file and move on to the next file.</summary>
        SkipFile,
        /// <summary>Stop the whole run. <see cref="ScriptRunResult.Stopped"/> is set on the result.</summary>
        Stop
    }

    /// <summary>Settings for a single <see cref="SqlScriptRunner.RunAsync"/> call.</summary>
    public sealed class ScriptRunOptions
    {
        public ScriptRunOptions(DatabaseProvider provider, string connectionString)
        {
            Provider = provider;
            ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <summary>Database engine to run against.</summary>
        public DatabaseProvider Provider { get; }

        /// <summary>Provider-specific ADO.NET connection string.</summary>
        public string ConnectionString { get; }

        /// <summary>
        /// Folder that holds the scripts. Relative entries in <see cref="Files"/> are resolved against it. When
        /// <see cref="Files"/> is empty, every top-level .sql and .txt file in this folder is run in name order.
        /// For <see cref="SqlScriptRunner.RunSqlAsync"/> it is the base folder for embedded file tags.
        /// </summary>
        public string? ScriptDirectory { get; set; }

        /// <summary>.sql scripts and .txt script lists to run, in order. Absolute or relative to <see cref="ScriptDirectory"/>.</summary>
        public IList<string> Files { get; set; } = new List<string>();

        /// <summary>Action taken when a statement fails and <see cref="OnError"/> is not set. Defaults to <see cref="ScriptErrorAction.Stop"/>.</summary>
        public ScriptErrorAction ErrorAction { get; set; } = ScriptErrorAction.Stop;

        /// <summary>Optional callback that decides, per failed statement, how the run proceeds. Overrides <see cref="ErrorAction"/>.</summary>
        public Func<ScriptStatementResult, ScriptErrorAction>? OnError { get; set; }

        /// <summary>Optional callback raised for each progress event (file started, statement succeeded/failed, warnings).</summary>
        public Action<ScriptRunEvent>? OnEvent { get; set; }

        /// <summary>Optional writer that receives a timestamped line for every event, e.g. a StreamWriter for a run log file.</summary>
        public TextWriter? Log { get; set; }

        /// <summary>Embedded file marker tags (tag to type). Null or empty uses <see cref="ScriptHelpers.DefaultMarkers"/>.</summary>
        public Dictionary<string, string>? EmbeddedFileMarkers { get; set; }

        /// <summary>When true, &lt;DnPTxt&gt; embedded files that contain JSON or XML are detected and normalized as such.</summary>
        public bool EnableEmbeddedTypeDetection { get; set; } = true;
    }
}
