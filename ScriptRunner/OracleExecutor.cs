/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using Oracle.ManagedDataAccess.Client;
using System;
using System.Linq;

namespace Dnp.ScriptRunner
{
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
}
