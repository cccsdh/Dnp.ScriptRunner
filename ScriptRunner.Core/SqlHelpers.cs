/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Dnp.ScriptRunner
{
    // Distinguishes the handful of literal-formatting rules (booleans, binary, dates) that vary by provider
    enum SqlLiteralProvider
    {
        Generic,
        Postgres,
        Oracle,
        Db2
    }

    static class SqlHelpers
    {
        // Formats a CLR value read from a data reader as a SQL literal suitable for an INSERT statement
        public static string FormatSqlLiteral(object? value, SqlLiteralProvider provider = SqlLiteralProvider.Generic)
        {
            if (value == null || value is DBNull) return "NULL";

            switch (value)
            {
                case bool b:
                    return provider == SqlLiteralProvider.Postgres ? (b ? "TRUE" : "FALSE") : (b ? "1" : "0");
                case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                    return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "NULL";
                case DateTime dt:
                    var formatted = dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                    return provider == SqlLiteralProvider.Oracle
                        ? $"TO_TIMESTAMP('{formatted}', 'YYYY-MM-DD HH24:MI:SS.FF3')"
                        : $"'{formatted}'";
                case DateTimeOffset dto:
                    return FormatSqlLiteral(dto.DateTime, provider);
                case byte[] bytes:
                    var hex = Convert.ToHexString(bytes);
                    return provider switch
                    {
                        SqlLiteralProvider.Postgres => $"decode('{hex}', 'hex')",
                        SqlLiteralProvider.Oracle => $"HEXTORAW('{hex}')",
                        SqlLiteralProvider.Db2 => $"x'{hex}'",
                        _ => "0x" + hex
                    };
                case Guid g:
                    return $"'{g}'";
                default:
                    var s = value.ToString() ?? string.Empty;
                    return "'" + s.Replace("'", "''") + "'";
            }
        }

        // Splits a script into statements on lines containing only "GO"
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
    }
}
