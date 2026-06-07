/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using IBM.Data.DB2.Core;
using System;
using System.Linq;

namespace Dnp.ScriptRunner
{
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
}
