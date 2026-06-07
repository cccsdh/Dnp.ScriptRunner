/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Npgsql;
using Spectre.Console;
using System.Threading.Channels;
using MySql.Data.MySqlClient;
using Oracle.ManagedDataAccess.Client;
using IBM.Data.DB2.Core;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Data.Common;
using System.Xml;

namespace Dnp.ScriptRunner
{
    // Settings storage (added EmbeddedFileMarkers)
    public class Settings
    {
        public Dictionary<string, List<string>> Connections { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Directories { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string>? EmbeddedFileMarkers { get; set; } = null;
        public bool EnableEmbeddedTypeDetection { get; set; } = true;

        private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "script-runner-settings.json");
        private static string AppSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        public static Settings Load()
        {
            try
            {
                // Prefer script-runner-settings.json for user overrides; fall back to appsettings.json shipped with app
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<Settings>(json, opts) ?? new Settings();
                }

                if (File.Exists(AppSettingsPath))
                {
                    var json = File.ReadAllText(AppSettingsPath);
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<Settings>(json, opts) ?? new Settings();
                }

                return new Settings();
            }
            catch
            {
                return new Settings();
            }
        }

        public void Save()
        {
            try
            {
                var opts = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(this, opts);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }

        public List<string> GetConnections(string provider)
        {
            if (Connections.TryGetValue(provider, out var list)) return list;
            return new List<string>();
        }

        public List<string> GetDirectories(string provider)
        {
            if (Directories.TryGetValue(provider, out var list)) return list;
            return new List<string>();
        }

        public void AddConnection(string provider, string connection)
        {
            if (!Connections.TryGetValue(provider, out var list))
            {
                list = new List<string>();
                Connections[provider] = list;
            }
            if (!list.Contains(connection)) list.Add(connection);
        }

        public void AddDirectory(string provider, string directory)
        {
            if (!Directories.TryGetValue(provider, out var list))
            {
                list = new List<string>();
                Directories[provider] = list;
            }
            if (!list.Contains(directory)) list.Add(directory);
        }
    }

    // Database executor abstraction
    interface IDbExecutor : IDisposable
    {
        void Open(string connectionString);
        string ExecuteNonQuery(string sql);
        // Upsert helper for workflows JSON definitions
        string ExecuteUpsertWorkflow(string name, string jsonDefinition, int isProduction, int isActive, int isValid, DateTime createdAt, DateTime updatedAt, int version);
    }

    class SqlServerExecutor : IDbExecutor
    {
        private SqlConnection? _conn;
        public void Open(string connectionString) { _conn = new SqlConnection(connectionString); _conn.Open(); }
        public string ExecuteNonQuery(string sql)
        {
            using var cmd = _conn!.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 1800;
            try { cmd.ExecuteNonQuery(); return "Success"; } catch (Exception ex) { return ex.Message; }
        }

        public string ExecuteUpsertWorkflow(string name, string jsonDefinition, int isProduction, int isActive, int isValid, DateTime createdAt, DateTime updatedAt, int version)
        {
            try
            {
                using var tx = _conn!.BeginTransaction();
                using var cmd = _conn.CreateCommand(); cmd.Transaction = tx; cmd.CommandTimeout = 1800;

                cmd.CommandText = "SELECT COUNT(*) FROM Workflows WHERE Name = @Name";
                cmd.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar) { Value = name });
                var exists = Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
                cmd.Parameters.Clear();

                if (exists)
                {
                    cmd.CommandText = @"UPDATE Workflows SET JsonDefinition = @JsonDefinition, IsProduction = @IsProduction, IsActive = @IsActive, IsValid = @IsValid, UpdatedAt = @UpdatedAt, Version = @Version WHERE Name = @Name";
                    cmd.Parameters.Add(new SqlParameter("@JsonDefinition", SqlDbType.NVarChar) { Value = jsonDefinition });
                    cmd.Parameters.Add(new SqlParameter("@IsProduction", SqlDbType.Int) { Value = isProduction });
                    cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Int) { Value = isActive });
                    cmd.Parameters.Add(new SqlParameter("@IsValid", SqlDbType.Int) { Value = isValid });
                    cmd.Parameters.Add(new SqlParameter("@UpdatedAt", SqlDbType.DateTime2) { Value = updatedAt });
                    cmd.Parameters.Add(new SqlParameter("@Version", SqlDbType.Int) { Value = version });
                    cmd.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar) { Value = name });
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    cmd.CommandText = @"INSERT INTO Workflows (Name, JsonDefinition, IsProduction, IsActive, IsValid, CreatedAt, UpdatedAt, Version) VALUES (@Name, @JsonDefinition, @IsProduction, @IsActive, @IsValid, @CreatedAt, @UpdatedAt, @Version)";
                    cmd.Parameters.Add(new SqlParameter("@Name", SqlDbType.NVarChar) { Value = name });
                    cmd.Parameters.Add(new SqlParameter("@JsonDefinition", SqlDbType.NVarChar) { Value = jsonDefinition });
                    cmd.Parameters.Add(new SqlParameter("@IsProduction", SqlDbType.Int) { Value = isProduction });
                    cmd.Parameters.Add(new SqlParameter("@IsActive", SqlDbType.Int) { Value = isActive });
                    cmd.Parameters.Add(new SqlParameter("@IsValid", SqlDbType.Int) { Value = isValid });
                    cmd.Parameters.Add(new SqlParameter("@CreatedAt", SqlDbType.DateTime2) { Value = createdAt });
                    cmd.Parameters.Add(new SqlParameter("@UpdatedAt", SqlDbType.DateTime2) { Value = updatedAt });
                    cmd.Parameters.Add(new SqlParameter("@Version", SqlDbType.Int) { Value = version });
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return "Success";
            }
            catch (Exception ex) { return ex.Message; }
        }

        public void Dispose() { try { _conn?.Close(); _conn?.Dispose(); } catch { } }
    }

    class PostgresExecutor : IDbExecutor
    {
        private NpgsqlConnection? _conn;
        public void Open(string connectionString) { _conn = new NpgsqlConnection(connectionString); _conn.Open(); }
        public string ExecuteNonQuery(string sql)
        {
            using var cmd = _conn!.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 1800;
            try { cmd.ExecuteNonQuery(); return "Success"; } catch (Exception ex) { return ex.Message; }
        }
        public string ExecuteUpsertWorkflow(string name, string jsonDefinition, int isProduction, int isActive, int isValid, DateTime createdAt, DateTime updatedAt, int version)
        {
            try
            {
                using var tx = _conn!.BeginTransaction();
                using var cmd = _conn.CreateCommand(); cmd.Transaction = tx; cmd.CommandTimeout = 1800;

                cmd.CommandText = "SELECT COUNT(*) FROM Workflows WHERE Name = @Name";
                cmd.Parameters.Add(new NpgsqlParameter("@Name", NpgsqlTypes.NpgsqlDbType.Text) { Value = name });
                var exists = Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
                cmd.Parameters.Clear();

                if (exists)
                {
                    cmd.CommandText = @"UPDATE Workflows SET JsonDefinition = @JsonDefinition, IsProduction = @IsProduction, IsActive = @IsActive, IsValid = @IsValid, UpdatedAt = @UpdatedAt, Version = @Version WHERE Name = @Name";
                    cmd.Parameters.Add(new NpgsqlParameter("@JsonDefinition", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = jsonDefinition });
                    cmd.Parameters.Add(new NpgsqlParameter("@IsProduction", NpgsqlTypes.NpgsqlDbType.Integer) { Value = isProduction });
                    cmd.Parameters.Add(new NpgsqlParameter("@IsActive", NpgsqlTypes.NpgsqlDbType.Integer) { Value = isActive });
                    cmd.Parameters.Add(new NpgsqlParameter("@IsValid", NpgsqlTypes.NpgsqlDbType.Integer) { Value = isValid });
                    cmd.Parameters.Add(new NpgsqlParameter("@UpdatedAt", NpgsqlTypes.NpgsqlDbType.Timestamp) { Value = updatedAt });
                    cmd.Parameters.Add(new NpgsqlParameter("@Version", NpgsqlTypes.NpgsqlDbType.Integer) { Value = version });
                    cmd.Parameters.Add(new NpgsqlParameter("@Name", NpgsqlTypes.NpgsqlDbType.Text) { Value = name });
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    cmd.CommandText = @"INSERT INTO Workflows (Name, JsonDefinition, IsProduction, IsActive, IsValid, CreatedAt, UpdatedAt, Version) VALUES (@Name, @JsonDefinition, @IsProduction, @IsActive, @IsValid, @CreatedAt, @UpdatedAt, @Version)";
                    cmd.Parameters.Add(new NpgsqlParameter("@Name", NpgsqlTypes.NpgsqlDbType.Text) { Value = name });
                    cmd.Parameters.Add(new NpgsqlParameter("@JsonDefinition", NpgsqlTypes.NpgsqlDbType.Jsonb) { Value = jsonDefinition });
                    cmd.Parameters.Add(new NpgsqlParameter("@IsProduction", NpgsqlTypes.NpgsqlDbType.Integer) { Value = isProduction });
                    cmd.Parameters.Add(new NpgsqlParameter("@IsActive", NpgsqlTypes.NpgsqlDbType.Integer) { Value = isActive });
                    cmd.Parameters.Add(new NpgsqlParameter("@IsValid", NpgsqlTypes.NpgsqlDbType.Integer) { Value = isValid });
                    cmd.Parameters.Add(new NpgsqlParameter("@CreatedAt", NpgsqlTypes.NpgsqlDbType.Timestamp) { Value = createdAt });
                    cmd.Parameters.Add(new NpgsqlParameter("@UpdatedAt", NpgsqlTypes.NpgsqlDbType.Timestamp) { Value = updatedAt });
                    cmd.Parameters.Add(new NpgsqlParameter("@Version", NpgsqlTypes.NpgsqlDbType.Integer) { Value = version });
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return "Success";
            }
            catch (Exception ex) { return ex.Message; }
        }
        public void Dispose() { try { _conn?.Close(); _conn?.Dispose(); } catch { } }
    }

    class SqliteExecutor : IDbExecutor
    {
        private SqliteConnection? _conn;

        public void Open(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("connectionString is required", nameof(connectionString));

            // If the input looks like a raw file path (no '='), build a proper connection string
            if (!connectionString.Contains('='))
            {
                var builder = new SqliteConnectionStringBuilder { DataSource = connectionString };
                connectionString = builder.ToString();
            }

            _conn = new SqliteConnection(connectionString);
            _conn.Open();
        }

        public string ExecuteNonQuery(string sql)
        {
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 1800;
            try { cmd.ExecuteNonQuery(); return "Success"; } catch (Exception ex) { return ex.Message; }
        }

        public string ExecuteUpsertWorkflow(string name, string jsonDefinition, int isProduction, int isActive, int isValid, DateTime createdAt, DateTime updatedAt, int version)
        {
            try
            {
                using var tx = _conn!.BeginTransaction();
                using var cmd = _conn.CreateCommand(); cmd.Transaction = tx; cmd.CommandTimeout = 1800;

                cmd.CommandText = "SELECT COUNT(*) FROM Workflows WHERE Name = @Name";
                cmd.Parameters.Add(new SqliteParameter("@Name", name));
                var exists = Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
                cmd.Parameters.Clear();

                if (exists)
                {
                    cmd.CommandText = @"UPDATE Workflows SET JsonDefinition = @JsonDefinition, IsProduction = @IsProduction, IsActive = @IsActive, IsValid = @IsValid, UpdatedAt = @UpdatedAt, Version = @Version WHERE Name = @Name";
                    cmd.Parameters.Add(new SqliteParameter("@JsonDefinition", jsonDefinition));
                    cmd.Parameters.Add(new SqliteParameter("@IsProduction", isProduction));
                    cmd.Parameters.Add(new SqliteParameter("@IsActive", isActive));
                    cmd.Parameters.Add(new SqliteParameter("@IsValid", isValid));
                    cmd.Parameters.Add(new SqliteParameter("@UpdatedAt", updatedAt));
                    cmd.Parameters.Add(new SqliteParameter("@Version", version));
                    cmd.Parameters.Add(new SqliteParameter("@Name", name));
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    cmd.CommandText = @"INSERT INTO Workflows (Name, JsonDefinition, IsProduction, IsActive, IsValid, CreatedAt, UpdatedAt, Version) VALUES (@Name, @JsonDefinition, @IsProduction, @IsActive, @IsValid, @CreatedAt, @UpdatedAt, @Version)";
                    cmd.Parameters.Add(new SqliteParameter("@Name", name));
                    cmd.Parameters.Add(new SqliteParameter("@JsonDefinition", jsonDefinition));
                    cmd.Parameters.Add(new SqliteParameter("@IsProduction", isProduction));
                    cmd.Parameters.Add(new SqliteParameter("@IsActive", isActive));
                    cmd.Parameters.Add(new SqliteParameter("@IsValid", isValid));
                    cmd.Parameters.Add(new SqliteParameter("@CreatedAt", createdAt));
                    cmd.Parameters.Add(new SqliteParameter("@UpdatedAt", updatedAt));
                    cmd.Parameters.Add(new SqliteParameter("@Version", version));
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return "Success";
            }
            catch (Exception ex) { return ex.Message; }
        }

        public void Dispose() { try { _conn?.Close(); _conn?.Dispose(); } catch { } }
    }

    class MySqlExecutor : IDbExecutor
    {
        private MySqlConnection? _conn;
        public void Open(string connectionString) { _conn = new MySqlConnection(connectionString); _conn.Open(); }
        public string ExecuteNonQuery(string sql)
        {
            using var cmd = _conn!.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 1800;
            try { cmd.ExecuteNonQuery(); return "Success"; } catch (Exception ex) { return ex.Message; }
        }
        public string ExecuteUpsertWorkflow(string name, string jsonDefinition, int isProduction, int isActive, int isValid, DateTime createdAt, DateTime updatedAt, int version)
        {
            try
            {
                using var tx = _conn!.BeginTransaction();
                using var cmd = _conn.CreateCommand(); cmd.Transaction = tx; cmd.CommandTimeout = 1800;

                cmd.CommandText = "SELECT COUNT(*) FROM Workflows WHERE Name = @Name";
                cmd.Parameters.Add(new MySqlParameter("@Name", name));
                var exists = Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
                cmd.Parameters.Clear();

                if (exists)
                {
                    cmd.CommandText = @"UPDATE Workflows SET JsonDefinition = @JsonDefinition, IsProduction = @IsProduction, IsActive = @IsActive, IsValid = @IsValid, UpdatedAt = @UpdatedAt, Version = @Version WHERE Name = @Name";
                    cmd.Parameters.Add(new MySqlParameter("@JsonDefinition", jsonDefinition));
                    cmd.Parameters.Add(new MySqlParameter("@IsProduction", isProduction));
                    cmd.Parameters.Add(new MySqlParameter("@IsActive", isActive));
                    cmd.Parameters.Add(new MySqlParameter("@IsValid", isValid));
                    cmd.Parameters.Add(new MySqlParameter("@UpdatedAt", updatedAt));
                    cmd.Parameters.Add(new MySqlParameter("@Version", version));
                    cmd.Parameters.Add(new MySqlParameter("@Name", name));
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    cmd.CommandText = @"INSERT INTO Workflows (Name, JsonDefinition, IsProduction, IsActive, IsValid, CreatedAt, UpdatedAt, Version) VALUES (@Name, @JsonDefinition, @IsProduction, @IsActive, @IsValid, @CreatedAt, @UpdatedAt, @Version)";
                    cmd.Parameters.Add(new MySqlParameter("@Name", name));
                    cmd.Parameters.Add(new MySqlParameter("@JsonDefinition", jsonDefinition));
                    cmd.Parameters.Add(new MySqlParameter("@IsProduction", isProduction));
                    cmd.Parameters.Add(new MySqlParameter("@IsActive", isActive));
                    cmd.Parameters.Add(new MySqlParameter("@IsValid", isValid));
                    cmd.Parameters.Add(new MySqlParameter("@CreatedAt", createdAt));
                    cmd.Parameters.Add(new MySqlParameter("@UpdatedAt", updatedAt));
                    cmd.Parameters.Add(new MySqlParameter("@Version", version));
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return "Success";
            }
            catch (Exception ex) { return ex.Message; }
        }
        public void Dispose() { try { _conn?.Close(); _conn?.Dispose(); } catch { } }
    }

    class OracleExecutor : IDbExecutor
    {
        private OracleConnection? _conn;
        public void Open(string connectionString) { _conn = new OracleConnection(connectionString); _conn.Open(); }
        public string ExecuteNonQuery(string sql)
        {
            using var cmd = _conn!.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 1800;
            try { cmd.ExecuteNonQuery(); return "Success"; } catch (Exception ex) { return ex.Message; }
        }
        public string ExecuteUpsertWorkflow(string name, string jsonDefinition, int isProduction, int isActive, int isValid, DateTime createdAt, DateTime updatedAt, int version)
        {
            try
            {
                using var tx = _conn!.BeginTransaction();
                using var cmd = _conn.CreateCommand(); cmd.Transaction = tx; cmd.CommandTimeout = 1800;

                cmd.CommandText = "SELECT COUNT(*) FROM Workflows WHERE Name = :Name";
                cmd.Parameters.Add(new OracleParameter(":Name", name));
                var exists = Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
                cmd.Parameters.Clear();

                if (exists)
                {
                    cmd.CommandText = @"UPDATE Workflows SET JsonDefinition = :JsonDefinition, IsProduction = :IsProduction, IsActive = :IsActive, IsValid = :IsValid, UpdatedAt = :UpdatedAt, Version = :Version WHERE Name = :Name";
                    cmd.Parameters.Add(new OracleParameter(":JsonDefinition", jsonDefinition));
                    cmd.Parameters.Add(new OracleParameter(":IsProduction", isProduction));
                    cmd.Parameters.Add(new OracleParameter(":IsActive", isActive));
                    cmd.Parameters.Add(new OracleParameter(":IsValid", isValid));
                    cmd.Parameters.Add(new OracleParameter(":UpdatedAt", updatedAt));
                    cmd.Parameters.Add(new OracleParameter(":Version", version));
                    cmd.Parameters.Add(new OracleParameter(":Name", name));
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    cmd.CommandText = @"INSERT INTO Workflows (Name, JsonDefinition, IsProduction, IsActive, IsValid, CreatedAt, UpdatedAt, Version) VALUES (:Name, :JsonDefinition, :IsProduction, :IsActive, :IsValid, :CreatedAt, :UpdatedAt, :Version)";
                    cmd.Parameters.Add(new OracleParameter(":Name", name));
                    cmd.Parameters.Add(new OracleParameter(":JsonDefinition", jsonDefinition));
                    cmd.Parameters.Add(new OracleParameter(":IsProduction", isProduction));
                    cmd.Parameters.Add(new OracleParameter(":IsActive", isActive));
                    cmd.Parameters.Add(new OracleParameter(":IsValid", isValid));
                    cmd.Parameters.Add(new OracleParameter(":CreatedAt", createdAt));
                    cmd.Parameters.Add(new OracleParameter(":UpdatedAt", updatedAt));
                    cmd.Parameters.Add(new OracleParameter(":Version", version));
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return "Success";
            }
            catch (Exception ex) { return ex.Message; }
        }
        public void Dispose() { try { _conn?.Close(); _conn?.Dispose(); } catch { } }
    }

    class Db2Executor : IDbExecutor
    {
        private DB2Connection? _conn;
        public void Open(string connectionString) { _conn = new DB2Connection(connectionString); _conn.Open(); }
        public string ExecuteNonQuery(string sql)
        {
            using var cmd = _conn!.CreateCommand(); cmd.CommandText = sql; cmd.CommandTimeout = 1800;
            try { cmd.ExecuteNonQuery(); return "Success"; } catch (Exception ex) { return ex.Message; }
        }
        public string ExecuteUpsertWorkflow(string name, string jsonDefinition, int isProduction, int isActive, int isValid, DateTime createdAt, DateTime updatedAt, int version)
        {
            try
            {
                using var tx = _conn!.BeginTransaction();
                using var cmd = _conn.CreateCommand(); cmd.Transaction = tx; cmd.CommandTimeout = 1800;

                cmd.CommandText = "SELECT COUNT(*) FROM Workflows WHERE Name = @Name";
                var p = cmd.CreateParameter(); p.ParameterName = "@Name"; p.Value = name; cmd.Parameters.Add(p);
                var exists = Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
                cmd.Parameters.Clear();

                if (exists)
                {
                    cmd.CommandText = @"UPDATE Workflows SET JsonDefinition = @JsonDefinition, IsProduction = @IsProduction, IsActive = @IsActive, IsValid = @IsValid, UpdatedAt = @UpdatedAt, Version = @Version WHERE Name = @Name";
                    var pj = cmd.CreateParameter(); pj.ParameterName = "@JsonDefinition"; pj.Value = jsonDefinition; cmd.Parameters.Add(pj);
                    var pp = cmd.CreateParameter(); pp.ParameterName = "@IsProduction"; pp.Value = isProduction; cmd.Parameters.Add(pp);
                    var pa = cmd.CreateParameter(); pa.ParameterName = "@IsActive"; pa.Value = isActive; cmd.Parameters.Add(pa);
                    var pv = cmd.CreateParameter(); pv.ParameterName = "@IsValid"; pv.Value = isValid; cmd.Parameters.Add(pv);
                    var pu = cmd.CreateParameter(); pu.ParameterName = "@UpdatedAt"; pu.Value = updatedAt; cmd.Parameters.Add(pu);
                    var pv2 = cmd.CreateParameter(); pv2.ParameterName = "@Version"; pv2.Value = version; cmd.Parameters.Add(pv2);
                    var pn = cmd.CreateParameter(); pn.ParameterName = "@Name"; pn.Value = name; cmd.Parameters.Add(pn);
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    cmd.CommandText = @"INSERT INTO Workflows (Name, JsonDefinition, IsProduction, IsActive, IsValid, CreatedAt, UpdatedAt, Version) VALUES (@Name, @JsonDefinition, @IsProduction, @IsActive, @IsValid, @CreatedAt, @UpdatedAt, @Version)";
                    var pn = cmd.CreateParameter(); pn.ParameterName = "@Name"; pn.Value = name; cmd.Parameters.Add(pn);
                    var pj = cmd.CreateParameter(); pj.ParameterName = "@JsonDefinition"; pj.Value = jsonDefinition; cmd.Parameters.Add(pj);
                    var pp = cmd.CreateParameter(); pp.ParameterName = "@IsProduction"; pp.Value = isProduction; cmd.Parameters.Add(pp);
                    var pa = cmd.CreateParameter(); pa.ParameterName = "@IsActive"; pa.Value = isActive; cmd.Parameters.Add(pa);
                    var pv = cmd.CreateParameter(); pv.ParameterName = "@IsValid"; pv.Value = isValid; cmd.Parameters.Add(pv);
                    var pc = cmd.CreateParameter(); pc.ParameterName = "@CreatedAt"; pc.Value = createdAt; cmd.Parameters.Add(pc);
                    var pu = cmd.CreateParameter(); pu.ParameterName = "@UpdatedAt"; pu.Value = updatedAt; cmd.Parameters.Add(pu);
                    var pv2 = cmd.CreateParameter(); pv2.ParameterName = "@Version"; pv2.Value = version; cmd.Parameters.Add(pv2);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return "Success";
            }
            catch (Exception ex) { return ex.Message; }
        }
        public void Dispose() { try { _conn?.Close(); _conn?.Dispose(); } catch { } }
    }

    static class Helpers
    {
        // Update header: copyright symbol then year with no space and ensure it's printed once at top
        public static void RenderHeader()
        {
            var year = DateTime.Now.Year;
            var rule = new Spectre.Console.Rule($"[bold yellow]Dnp.ScriptRunner ©{year} Doughnuts Publishing[/]") { Justification = Spectre.Console.Justify.Center };
            AnsiConsole.Write(rule);
            // add a separator line so subsequent output appears below the header
            AnsiConsole.WriteLine();
        }

        public static IEnumerable<string> SplitStatements(string sql)
        {
            var sb = new StringBuilder();
            using var reader = new StringReader(sql);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
                {
                    var s = sb.ToString().Trim();
                    if (!string.IsNullOrEmpty(s)) yield return s;
                    sb.Clear();
                }
                else
                {
                    sb.AppendLine(line);
                }
            }

            var last = sb.ToString().Trim();
            if (!string.IsNullOrEmpty(last)) yield return last;
        }

        public static List<string> ExpandFiles(IEnumerable<string> selectedFiles, string baseDirectory)
        {
            var result = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in selectedFiles)
            {
                var full = Path.GetFullPath(Path.Combine(baseDirectory, f));
                ExpandRecursive(full, result, visited);
            }
            return result;
        }

        public static void ExpandRecursive(string filePath, List<string> output, HashSet<string> visited)
        {
            if (!File.Exists(filePath)) return;
            if (visited.Contains(filePath)) return;
            visited.Add(filePath);
            if (filePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                output.Add(filePath);
                return;
            }

            if (filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in File.ReadAllLines(filePath))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;
                    if (trimmed.StartsWith(">>"))
                    {
                        var referenced = trimmed.Substring(2).Trim();
                        var referencedFull = Path.IsPathRooted(referenced)
                            ? referenced
                            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, referenced));
                        ExpandRecursive(referencedFull, output, visited);
                    }
                    else
                    {
                        var candidate = Path.IsPathRooted(trimmed)
                            ? trimmed
                            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, trimmed));
                        if (File.Exists(candidate) && candidate.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                            output.Add(candidate);
                    }
                }
            }
        }

        // Process files and write progress to provided TextWriter
        public static async Task ProcessPathAsync(string filePath, IDbExecutor executor, HashSet<string> visited, CancellationToken ct, TextWriter writer)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

            if (!File.Exists(filePath))
            {
                var msg = $"File not found: {filePath}";
                AnsiConsole.MarkupLine($"[yellow]{msg}[/]");
                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}");
                return;
            }

            if (visited.Contains(filePath)) return;
            visited.Add(filePath);

            var ext = Path.GetExtension(filePath) ?? string.Empty;
            if (ext.Equals(".sql", StringComparison.OrdinalIgnoreCase))
            {
                var headerMsg = $"Executing SQL file: {Path.GetFileName(filePath)}";
                AnsiConsole.MarkupLine($"[green]{headerMsg}[/]");
                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {headerMsg}");

                var content = await File.ReadAllTextAsync(filePath, ct);
                var statements = SplitStatements(content).ToList();
                var baseDir = Path.GetDirectoryName(filePath) ?? string.Empty;
                for (var i = 0; i < statements.Count; i++)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    var sql = statements[i];

                    // load markers from settings (configurable)
                    var settings = Settings.Load();
                    var markers = settings.EmbeddedFileMarkers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    // Replace any embedded file tags in the statement (applies to countries inserts as well)
                    try
                    {
                        sql = ScriptHelpers.ReplaceEmbeddedTags(sql, baseDir, markers, settings.EnableEmbeddedTypeDetection);
                    }
                    catch (Exception ex)
                    {
                        var err = $"Failed to replace embedded tags: {ex.Message}";
                        AnsiConsole.MarkupLine($"[red]{err}[/]");
                        await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {err}");
                        continue;
                    }

                    // Special handling: INSERT INTO Workflows with embedded file markers
                    if (sql.IndexOf("INSERT INTO Workflows", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (ScriptHelpers.TryExtractEmbeddedFileTag(sql, markers, out var openTag, out var closeTag, out var relativePath, out var fileType))
                        {
                            // After replacement, the JSON content is already inlined and normalized, so parse and upsert as before
                            var parsed = ScriptHelpers.ParseWorkflowInsert(sql);
                            if (parsed == null)
                            {
                                var err = $"Unable to parse INSERT statement for workflow values in file {Path.GetFileName(filePath)}";
                                AnsiConsole.MarkupLine($"[red]{err}[/]");
                                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {err}");
                                continue;
                            }

                            // Extract the JsonDefinition literal from the sql by finding the first single-quoted JSON value after Name
                            // Best-effort: find the first '{' after the Name occurrence
                            var jsonStart = sql.IndexOf('{');
                            var jsonEnd = sql.LastIndexOf('}');
                            string jsonContent = string.Empty;
                            if (jsonStart >= 0 && jsonEnd > jsonStart)
                            {
                                jsonContent = sql.Substring(jsonStart, jsonEnd - jsonStart + 1);
                            }

                            if (string.IsNullOrWhiteSpace(jsonContent))
                            {
                                var err = $"Unable to extract JSON content for workflow '{parsed.Name}' in file {Path.GetFileName(filePath)}";
                                AnsiConsole.MarkupLine($"[red]{err}[/]");
                                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {err}");
                                continue;
                            }

                            // validate and normalize json
                            try
                            {
                                using var doc = JsonDocument.Parse(jsonContent);
                                jsonContent = JsonSerializer.Serialize(doc.RootElement);
                            }
                            catch (Exception ex)
                            {
                                var err = $"Failed to parse JSON content for workflow '{parsed.Name}': {ex.Message}";
                                AnsiConsole.MarkupLine($"[red]{err}[/]");
                                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {err}");
                                continue;
                            }

                            var createdAt = parsed.CreatedAt ?? DateTime.UtcNow;
                            var updatedAt = parsed.UpdatedAt ?? DateTime.UtcNow;

                            var res = executor.ExecuteUpsertWorkflow(parsed.Name, jsonContent, parsed.IsProduction, parsed.IsActive, parsed.IsValid, createdAt, updatedAt, parsed.Version);
                            var resultMsg = res == "Success" ? $"OK Upsert workflow '{parsed.Name}'" : $"ERROR Upsert workflow '{parsed.Name}': {res}";
                            if (res == "Success") AnsiConsole.MarkupLine($" [green]{resultMsg}[/]"); else AnsiConsole.MarkupLine($" [red]{resultMsg}[/]");
                            await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {resultMsg}");

                            continue;
                        }
                    }

                    var res2 = executor.ExecuteNonQuery(sql);
                    var resultMsg2 = res2 == "Success"
                        ? $"OK Statement {i + 1}/{statements.Count}"
                        : $"ERROR Statement {i + 1}/{statements.Count}: {res2}";

                    if (res2 == "Success")
                        AnsiConsole.MarkupLine($" [green]{resultMsg2}[/]");
                    else
                        AnsiConsole.MarkupLine($" [red]{resultMsg2}[/]");

                    await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {resultMsg2}");

                    if (res2 != "Success")
                    {
                        var action = AnsiConsole.Prompt(new SelectionPrompt<string>().AddChoices(new[] { "Continue", "Skip File", "Stop" }));
                        if (action == "Stop") ct.ThrowIfCancellationRequested();
                        if (action == "Skip File") break;
                    }
                }
                return;
            }

            if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var headerMsg = $"Processing list file: {Path.GetFileName(filePath)}";
                AnsiConsole.MarkupLine($"[green]{headerMsg}[/]");
                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {headerMsg}");

                var lines = await File.ReadAllLinesAsync(filePath, ct);
                foreach (var raw in lines)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    var line = raw.Trim();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith(">>"))
                    {
                        var referenced = line.Substring(2).Trim();
                        var referencedFull = Path.IsPathRooted(referenced)
                            ? referenced
                            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, referenced));
                        await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Nested list reference: {referencedFull}");
                        await ProcessPathAsync(referencedFull, executor, visited, ct, writer);
                    }
                    else
                    {
                        var candidate = Path.IsPathRooted(line)
                            ? line
                            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, line));

                        var candidateExt = Path.GetExtension(candidate) ?? string.Empty;
                        if (candidateExt.Equals(".sql", StringComparison.OrdinalIgnoreCase) || candidateExt.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                        {
                            await ProcessPathAsync(candidate, executor, visited, ct, writer);
                        }
                        else
                        {
                            if (File.Exists(candidate)) await ProcessPathAsync(candidate, executor, visited, ct, writer);
                            else
                            {
                                var ignored = $"Ignored line (not a file): {line} in {Path.GetFileName(filePath)}";
                                AnsiConsole.MarkupLine($"[yellow]{ignored}[/]");
                                await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ignored}");
                            }
                        }
                    }
                }
                return;
            }

            var unsupported = $"Unsupported file type: {filePath}";
            AnsiConsole.MarkupLine($"[yellow]{unsupported}[/]");
            await writer.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {unsupported}");
        }
    }

    // Application entrypoint
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Helpers.RenderHeader();

            var settings = Settings.Load();

            var dbType = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select database type")
                    .AddChoices(new[] { "PostgreSQL", "SqlServer", "Sqlite", "MySQL", "Oracle", "DB2" }));

            // Connection string: offer saved connections per provider
            string connectionString;
            var savedConns = settings.GetConnections(dbType);
            if (savedConns.Count > 0)
            {
                var connChoices = new List<string>(savedConns);
                connChoices.Add("<New connection...>");
                var pick = AnsiConsole.Prompt(
                    new SelectionPrompt<string>().Title("Select a saved connection or create new").AddChoices(connChoices));
                if (pick == "<New connection...>")
                {
                    connectionString = AnsiConsole.Prompt(
                        new TextPrompt<string>("Enter connection string")
                            .Validate(s => string.IsNullOrWhiteSpace(s) ? ValidationResult.Error("[red]Connection string required[/]") : ValidationResult.Success()));
                    // Auto-save new connection
                    settings.AddConnection(dbType, connectionString);
                    settings.Save();
                }
                else
                {
                    connectionString = pick;
                }
            }
            else
            {
                connectionString = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter connection string")
                        .Validate(s => string.IsNullOrWhiteSpace(s) ? ValidationResult.Error("[red]Connection string required[/]") : ValidationResult.Success()));
                // Auto-save when no saved connections exist
                settings.AddConnection(dbType, connectionString);
                settings.Save();
            }

            // Script directory: offer saved directories per provider
            string scriptDir;
            var savedDirs = settings.GetDirectories(dbType);
            if (savedDirs.Count > 0)
            {
                var dirChoices = new List<string>(savedDirs);
                dirChoices.Add("<New directory...>");
                var pickDir = AnsiConsole.Prompt(
                    new SelectionPrompt<string>().Title("Select a saved scripts directory or create new").AddChoices(dirChoices));
                if (pickDir == "<New directory...>")
                {
                    scriptDir = AnsiConsole.Prompt(
                        new TextPrompt<string>("Scripts directory (full path)")
                            .DefaultValue(Environment.CurrentDirectory)
                            .Validate(s => System.IO.Directory.Exists(s) ? ValidationResult.Success() : ValidationResult.Error("[red]Directory not found[/]")));
                    // Auto-save new directory
                    settings.AddDirectory(dbType, scriptDir);
                    settings.Save();
                }
                else
                {
                    scriptDir = pickDir;
                }
            }
            else
            {
                scriptDir = AnsiConsole.Prompt(
                    new TextPrompt<string>("Scripts directory (full path)")
                        .DefaultValue(Environment.CurrentDirectory)
                        .Validate(s => System.IO.Directory.Exists(s) ? ValidationResult.Success() : ValidationResult.Error("[red]Directory not found[/]")));
                // Auto-save when no saved directories exist
                settings.AddDirectory(dbType, scriptDir);
                settings.Save();
            }

            var files = System.IO.Directory.EnumerateFiles(scriptDir, "*.*", System.IO.SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .Select(f => System.IO.Path.GetFileName(f))
                .OrderBy(n => n)
                .ToList();

            if (!files.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No .sql or .txt files found in directory.[/]");
                return 0;
            }

            var selected = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Select files to include (use [green]<space>[/] to toggle). You can select .sql and .txt")
                    .PageSize(10)
                    .AddChoices(files));

            if (selected == null || selected.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No files selected. Exiting.[/]");
                return 0;
            }

            // Setup agent channel
            var channel = Channel.CreateUnbounded<Func<Task>>();
            var cts = new CancellationTokenSource();

            // Start agent runner
            var agent = Task.Run(async () =>
            {
                await foreach (var work in channel.Reader.ReadAllAsync(cts.Token))
                {
                    try
                    {
                        await work();
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Agent work exception:[/] {ex.Message}");
                    }
                }
            }, cts.Token);

            IDbExecutor executor = dbType switch
            {
                "PostgreSQL" => new PostgresExecutor(),
                "SqlServer" => new SqlServerExecutor(),
                "Sqlite" => new SqliteExecutor(),
                "MySQL" => new MySqlExecutor(),
                "Oracle" => new OracleExecutor(),
                "DB2" => new Db2Executor(),
                _ => throw new InvalidOperationException("Unsupported database")
            };

            try
            {
                AnsiConsole.Status().Start("Opening connection...", ctx => executor.Open(connectionString));
                AnsiConsole.MarkupLine("[green]Connection opened.[/]");

                // Preserve choice order (files list order) when queuing selected items
                var selectedSet = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
                var orderedSelectedFullPaths = files
                    .Where(f => selectedSet.Contains(f))
                    .Select(f => Path.GetFullPath(Path.Combine(scriptDir, f)))
                    .ToList();

                var visitedGlobal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var total = orderedSelectedFullPaths.Count;
                var index = 0;

                // Create single output log file for the run
                var outputFile = Path.Combine(scriptDir, $"script-run-results-{DateTime.Now:yyyyMMdd_HHmmss}.log");
                using var fileWriter = new StreamWriter(outputFile, false, Encoding.UTF8) { AutoFlush = true };

                // Queue one work item per top-level selected file. The work item will process the file lines in order and recurse.
                foreach (var topFile in orderedSelectedFullPaths)
                {
                    index++;
                    var topFileName = Path.GetFileName(topFile);
                    var queuedMsg = $"({index}/{total}) Queuing {topFileName}";
                    AnsiConsole.MarkupLine($"[blue]{queuedMsg}[/]");
                    await fileWriter.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {queuedMsg}");

                    await channel.Writer.WriteAsync(async () =>
                    {
                        await Helpers.ProcessPathAsync(topFile, executor, visitedGlobal, cts.Token, fileWriter);
                    }, cts.Token);
                }

                // mark completion
                channel.Writer.Complete();
                await agent;

                // write completion note and close writer
                await fileWriter.WriteLineAsync($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] All selected scripts processed.");
                fileWriter.Close();

                Console.Clear();
                Helpers.RenderHeader();
                AnsiConsole.MarkupLine("[green]All selected scripts processed.[/]");

                var json = JsonSerializer.Serialize(orderedSelectedFullPaths);
                File.WriteAllText("output.json", json);
                AnsiConsole.MarkupLine($"[green]Run log written to[/] {outputFile}");

                if (AnsiConsole.Confirm("Do you want to open the run results now?"))
                {
                    try
                    {
                        var psi = new ProcessStartInfo(outputFile) { UseShellExecute = true };
                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Unable to open file:[/] {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                AnsiConsole.MarkupLine("[yellow]Execution cancelled.[/]");

                var cancellationInfo = new
                {
                    Timestamp = DateTime.UtcNow,
                    Status = "Cancelled",
                    ProcessedFiles = files.Count,
                    RemainingFiles = 0
                };

                var json = JsonSerializer.Serialize(cancellationInfo);
                File.WriteAllText("cancellation_info.json", json);
                AnsiConsole.MarkupLine("[green]Cancellation information written to cancellation_info.json[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Fatal error:[/] {ex.Message}");

                var errorInfo = new
                {
                    Timestamp = DateTime.UtcNow,
                    Status = "Error",
                    Message = ex.Message,
                    StackTrace = ex.StackTrace
                };

                var json = JsonSerializer.Serialize(errorInfo);
                File.WriteAllText("error_info.json", json);
                AnsiConsole.MarkupLine("[green]Error information written to error_info.json[/]");
            }
            finally
            {
                executor.Dispose();
            }

            return 0;
        }
    }
}
