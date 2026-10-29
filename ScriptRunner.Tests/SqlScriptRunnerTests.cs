using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Dnp.ScriptRunner.Tests
{
    // Exercises the public library API end to end against a temporary SQLite database
    [TestClass]
    public class SqlScriptRunnerTests
    {
        private string _dir = string.Empty;
        private string _conn = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _dir = Path.Combine(Path.GetTempPath(), "sr_lib_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            // Pooling off so the database file can be deleted during cleanup
            _conn = $"Data Source={Path.Combine(_dir, "test.db")};Pooling=False";
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private long Count(string table)
        {
            using var c = new SqliteConnection(_conn);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
            return (long)cmd.ExecuteScalar()!;
        }

        [TestMethod]
        public async Task RunAsync_Directory_RunsScriptsAndListsInOrder()
        {
            File.WriteAllText(Path.Combine(_dir, "01_create.sql"), "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT);");
            File.WriteAllText(Path.Combine(_dir, "02_insert.sql"), "INSERT INTO Items (Name) VALUES ('a');\nGO\nINSERT INTO Items (Name) VALUES ('b');");
            Directory.CreateDirectory(Path.Combine(_dir, "more"));
            File.WriteAllText(Path.Combine(_dir, "more", "03_more.sql"), "INSERT INTO Items (Name) VALUES ('c');");
            File.WriteAllText(Path.Combine(_dir, "03_list.txt"), "more/03_more.sql\n02_insert.sql\n");

            var events = new List<ScriptRunEvent>();
            var result = await SqlScriptRunner.RunAsync(new ScriptRunOptions(DatabaseProvider.Sqlite, _conn)
            {
                ScriptDirectory = _dir,
                OnEvent = events.Add
            });

            Assert.IsTrue(result.Success);
            Assert.AreEqual(4, result.StatementsSucceeded);
            // 02_insert.sql is referenced again by the list but only runs once
            Assert.AreEqual(3, result.ExecutedFiles.Count);
            Assert.AreEqual(3L, Count("Items"));
            Assert.AreEqual(ScriptRunEventKind.ConnectionOpened, events[0].Kind);
            Assert.AreEqual(ScriptRunEventKind.RunCompleted, events[^1].Kind);
        }

        [TestMethod]
        public async Task RunAsync_StopsOnFirstErrorByDefault()
        {
            File.WriteAllText(Path.Combine(_dir, "01.sql"), "CREATE TABLE T (Id INTEGER);\nGO\nINSERT INTO Missing VALUES (1);\nGO\nINSERT INTO T VALUES (1);");
            File.WriteAllText(Path.Combine(_dir, "02.sql"), "INSERT INTO T VALUES (2);");

            var result = await SqlScriptRunner.RunAsync(new ScriptRunOptions(DatabaseProvider.Sqlite, _conn) { ScriptDirectory = _dir });

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.Stopped);
            Assert.AreEqual(1, result.StatementsFailed);
            Assert.AreEqual(2, result.Failures[0].StatementNumber);
            Assert.AreEqual(0L, Count("T"));
        }

        [TestMethod]
        public async Task RunAsync_SkipFileMovesToNextFile()
        {
            File.WriteAllText(Path.Combine(_dir, "01.sql"), "CREATE TABLE T (Id INTEGER);\nGO\nINSERT INTO Missing VALUES (1);\nGO\nINSERT INTO T VALUES (1);");
            File.WriteAllText(Path.Combine(_dir, "02.sql"), "INSERT INTO T VALUES (2);");

            var result = await SqlScriptRunner.RunAsync(new ScriptRunOptions(DatabaseProvider.Sqlite, _conn)
            {
                ScriptDirectory = _dir,
                OnError = _ => ScriptErrorAction.SkipFile
            });

            Assert.IsFalse(result.Stopped);
            Assert.AreEqual(1, result.StatementsFailed);
            Assert.AreEqual(1L, Count("T"));
        }

        [TestMethod]
        public async Task RunAsync_ExplicitFilesRunOnlyThoseFiles()
        {
            File.WriteAllText(Path.Combine(_dir, "01.sql"), "CREATE TABLE T (Id INTEGER);");
            File.WriteAllText(Path.Combine(_dir, "02.sql"), "INSERT INTO T VALUES (1);");
            File.WriteAllText(Path.Combine(_dir, "03.sql"), "INSERT INTO T VALUES (2);");

            var result = await SqlScriptRunner.RunAsync(new ScriptRunOptions(DatabaseProvider.Sqlite, _conn)
            {
                ScriptDirectory = _dir,
                Files = { "01.sql", "03.sql" }
            });

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1L, Count("T"));
        }

        [TestMethod]
        public async Task RunSqlAsync_RunsInlineSqlWithEmbeddedFiles()
        {
            File.WriteAllText(Path.Combine(_dir, "doc.json"), "{ \"a\": 1 }");
            var sql = "CREATE TABLE Docs (Body TEXT);\nGO\nINSERT INTO Docs VALUES (<DnPJson>doc.json</DnPJson>);";

            var result = await SqlScriptRunner.RunSqlAsync(new ScriptRunOptions(DatabaseProvider.Sqlite, _conn) { ScriptDirectory = _dir }, sql);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.StatementsSucceeded);
            Assert.AreEqual(1L, Count("Docs"));
        }

        [TestMethod]
        public async Task GenerateSchemaScript_IncludesTablesAndData()
        {
            await SqlScriptRunner.RunSqlAsync(new ScriptRunOptions(DatabaseProvider.Sqlite, _conn),
                "CREATE TABLE People (Id INTEGER PRIMARY KEY, Name TEXT);\nGO\nINSERT INTO People (Name) VALUES ('Ada');");

            var script = SqlScriptRunner.GenerateSchemaScript(DatabaseProvider.Sqlite, _conn, includeData: true);

            StringAssert.Contains(script, "CREATE TABLE People");
            StringAssert.Contains(script, "'Ada'");
        }

        [TestMethod]
        public void TryParseProvider_IsCaseInsensitiveAndRejectsUnknown()
        {
            Assert.IsTrue(SqlScriptRunner.TryParseProvider("postgresql", out var p));
            Assert.AreEqual(DatabaseProvider.PostgreSQL, p);
            Assert.IsFalse(SqlScriptRunner.TryParseProvider("Access", out _));
            Assert.IsFalse(SqlScriptRunner.TryParseProvider("99", out _));
        }
    }
}
