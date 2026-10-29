/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using System;
using System.Collections.Generic;

namespace Dnp.ScriptRunner
{
    /// <summary>Kinds of progress event reported through <see cref="ScriptRunOptions.OnEvent"/>.</summary>
    public enum ScriptRunEventKind
    {
        ConnectionOpened,
        SqlFileStarted,
        ListFileStarted,
        NestedListReference,
        StatementSucceeded,
        StatementFailed,
        Warning,
        RunCompleted
    }

    /// <summary>A single progress event raised while a run is in progress.</summary>
    public sealed class ScriptRunEvent
    {
        internal ScriptRunEvent(ScriptRunEventKind kind, string message, string? filePath = null, ScriptStatementResult? statement = null)
        {
            Kind = kind;
            Message = message;
            FilePath = filePath;
            Statement = statement;
        }

        public ScriptRunEventKind Kind { get; }
        public string Message { get; }
        public string? FilePath { get; }
        /// <summary>Set for <see cref="ScriptRunEventKind.StatementSucceeded"/> and <see cref="ScriptRunEventKind.StatementFailed"/>.</summary>
        public ScriptStatementResult? Statement { get; }
        public DateTime Timestamp { get; } = DateTime.Now;
    }

    /// <summary>The outcome of one statement.</summary>
    public sealed class ScriptStatementResult
    {
        internal ScriptStatementResult(string filePath, int statementNumber, int statementCount, string sql, string? error)
        {
            FilePath = filePath;
            StatementNumber = statementNumber;
            StatementCount = statementCount;
            Sql = sql;
            Error = error;
        }

        /// <summary>Script file the statement came from (empty for inline SQL).</summary>
        public string FilePath { get; }
        /// <summary>1-based position of the statement within its file.</summary>
        public int StatementNumber { get; }
        public int StatementCount { get; }
        /// <summary>The statement as sent to the database, after embedded file tags were replaced.</summary>
        public string Sql { get; }
        /// <summary>The database error message, or null on success.</summary>
        public string? Error { get; }
        public bool Succeeded => Error == null;
    }

    /// <summary>Summary of a completed run.</summary>
    public sealed class ScriptRunResult
    {
        internal List<string> ExecutedFilesList { get; } = new();
        internal List<ScriptStatementResult> FailuresList { get; } = new();

        /// <summary>True when every statement succeeded and the run was not stopped.</summary>
        public bool Success => StatementsFailed == 0 && !Stopped;
        public int StatementsSucceeded { get; internal set; }
        public int StatementsFailed => FailuresList.Count;
        /// <summary>True when an error handler returned <see cref="ScriptErrorAction.Stop"/>.</summary>
        public bool Stopped { get; internal set; }
        /// <summary>Full paths of the .sql files that were executed, in order.</summary>
        public IReadOnlyList<string> ExecutedFiles => ExecutedFilesList;
        public IReadOnlyList<ScriptStatementResult> Failures => FailuresList;
        public TimeSpan Elapsed { get; internal set; }
    }
}
