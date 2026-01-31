using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Linq;
using System.Text.RegularExpressions;

namespace Dnp.ScriptRunner
{
    public static class ScriptHelpers
    {
        // default markers: tag -> type
        public static Dictionary<string, string> DefaultMarkers => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"<DnPJson>", "json"},
            {"</DnPJson>", "json_close"},
            {"<DnPXml>", "xml"},
            {"</DnPXml>", "xml_close"},
            {"<DnPTxt>", "txt"},
            {"</DnPTxt>", "txt_close"}
        };

        // Try to find any configured marker pair in the sql and extract the path and type
        public static bool TryExtractEmbeddedFileTag(string sql, Dictionary<string, string> markers, out string openTag, out string closeTag, out string relativePath, out string fileType)
        {
            openTag = closeTag = relativePath = fileType = string.Empty;
            if (markers == null || markers.Count == 0) markers = DefaultMarkers;

            // We expect markers to contain open and close tags; build pairs based on common prefixes
            var pairs = new List<(string open, string close, string type)>();
            var keys = markers.Keys.ToList();
            foreach (var k in keys)
            {
                if (k.StartsWith("</")) continue;
                var closeKey = "</" + k.TrimStart('<');
                if (markers.ContainsKey(closeKey))
                {
                    var type = markers[k];
                    pairs.Add((k, closeKey, type == "json" || type == "xml" ? type : "txt"));
                }
            }

            foreach (var p in pairs)
            {
                var idx = sql.IndexOf(p.open, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    var end = sql.IndexOf(p.close, idx + p.open.Length, StringComparison.OrdinalIgnoreCase);
                    if (end > idx)
                    {
                        openTag = p.open; closeTag = p.close; fileType = p.type;
                        relativePath = sql.Substring(idx + p.open.Length, end - (idx + p.open.Length)).Trim();
                        return true;
                    }
                }
            }

            return false;
        }

        public static string SanitizeEmbeddedContent(string content, string type)
        {
            if (type == null) type = "txt";
            type = type.ToLowerInvariant();
            if (type == "json")
            {
                using var doc = JsonDocument.Parse(content);
                return JsonSerializer.Serialize(doc.RootElement);
            }
            else if (type == "xml")
            {
                var doc = new XmlDocument();
                doc.LoadXml(content);
                using var sw = new StringWriter();
                using var xw = XmlWriter.Create(sw, new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true });
                doc.WriteTo(xw);
                xw.Flush();
                return sw.ToString();
            }
            else
            {
                // txt: return as-is but trim surrounding whitespace
                return content.Trim();
            }
        }

        // More robust parser using regex to extract Name and trailing numeric flags/version
        public static ParsedWorkflowInsert? ParseWorkflowInsert(string sql)
        {
            // Normalize whitespace
            var normalized = Regex.Replace(sql, @"\r?\n", " ", RegexOptions.Compiled);
            normalized = Regex.Replace(normalized, @"\s+", " ", RegexOptions.Compiled);

            // Find VALUES(...) block
            var m = Regex.Match(normalized, @"VALUES\s*\((.*)\)\s*;?", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var inside = m.Groups[1].Value.Trim();

            // Extract first quoted string as Name
            var nameMatch = Regex.Match(inside, "'((?:[^']|'')*)'");
            if (!nameMatch.Success) return null;
            var name = nameMatch.Groups[1].Value.Replace("''", "'");

            // Remove Name and everything up to the next comma
            var afterName = inside.Substring(nameMatch.Index + nameMatch.Length).TrimStart();
            if (afterName.StartsWith(",")) afterName = afterName.Substring(1).TrimStart();

            // Now we expect JsonDefinition as either a quoted string or an embedded tag - remove it
            if (afterName.StartsWith("'"))
            {
                // skip quoted string, find matching end
                var sb = new StringBuilder();
                bool inString = false;
                for (int i = 0; i < afterName.Length; i++)
                {
                    var c = afterName[i];
                    sb.Append(c);
                    if (c == '\'') inString = !inString;
                    if (!inString && c == ',')
                    {
                        afterName = afterName.Substring(i + 1).TrimStart();
                        break;
                    }
                }
            }
            else
            {
                // maybe embedded tag - try to strip up to closing ')</file>' or similar
                var idx = afterName.IndexOf('<');
                if (idx >= 0)
                {
                    var closeIdx = afterName.IndexOf('>', idx + 1);
                    // crude: find the next comma after the next closing tag sequence
                    var nextComma = afterName.IndexOf(',', idx + 1);
                    if (nextComma > 0) afterName = afterName.Substring(nextComma + 1).TrimStart();
                }
            }

            // Now split remaining by commas (top-level)
            var tokens = SplitTopLevelCommas(afterName).ToList();

            int isProduction = 0, isActive = 0, isValid = 0, version = 1;
            DateTime? createdAt = null, updatedAt = null;

            // Try to parse tokens left to right for known types
            var intVals = new List<int>();
            var dateVals = new List<DateTime?>();
            foreach (var t in tokens)
            {
                var tt = t.Trim();
                if (tt.Equals("NOW()", StringComparison.OrdinalIgnoreCase) || tt.Equals("CURRENT_TIMESTAMP", StringComparison.OrdinalIgnoreCase))
                {
                    dateVals.Add(DateTime.UtcNow);
                }
                else if (int.TryParse(StripTrailingComma(tt), out var iv))
                {
                    intVals.Add(iv);
                }
                else if (DateTime.TryParse(StripQuotes(tt), out var dt))
                {
                    dateVals.Add(dt);
                }
            }

            if (intVals.Count >= 4)
            {
                isProduction = intVals[0]; isActive = intVals[1]; isValid = intVals[2]; version = intVals[3];
            }
            else if (intVals.Count >= 3)
            {
                isProduction = intVals[0]; isActive = intVals[1]; isValid = intVals[2];
            }

            if (dateVals.Count >= 2)
            {
                createdAt = dateVals[0]; updatedAt = dateVals[1];
            }

            return new ParsedWorkflowInsert { Name = name, IsProduction = isProduction, IsActive = isActive, IsValid = isValid, CreatedAt = createdAt, UpdatedAt = updatedAt, Version = version };
        }

        private static IEnumerable<string> SplitTopLevelCommas(string input)
        {
            var sb = new StringBuilder();
            bool inString = false;
            int depth = 0;
            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (c == '\'') inString = !inString;
                if (!inString)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (c == ',' && depth == 0)
                    {
                        yield return sb.ToString(); sb.Clear(); continue;
                    }
                }
                sb.Append(c);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        private static string StripTrailingComma(string s) => s.Trim().TrimEnd(',');
        private static string StripQuotes(string s) => s.Trim().Trim('\'', '"');

        // Replace all embedded tags found in sql with sanitized, SQL-escaped single-quoted literals.
        public static string ReplaceEmbeddedTags(string sql, string baseDirectory, Dictionary<string, string>? markers = null, bool enableDetection = true)
        {
            if (markers == null || markers.Count == 0) markers = DefaultMarkers;
            var output = sql;
            // loop until no more tags found
            while (TryExtractEmbeddedFileTag(output, markers, out var openTag, out var closeTag, out var relativePath, out var fileType))
            {
                var filePath = Path.IsPathRooted(relativePath) ? relativePath : Path.GetFullPath(Path.Combine(baseDirectory ?? string.Empty, relativePath));
                string fileContent;
                try
                {
                    fileContent = File.ReadAllText(filePath);
                }
                catch
                {
                    // if file not found, replace with empty string literal to avoid SQL error
                    fileContent = string.Empty;
                }

                // if marker type is txt and detection enabled, try to detect JSON or XML
                if (string.Equals(fileType, "txt", StringComparison.OrdinalIgnoreCase) && enableDetection)
                {
                    var t = fileContent.TrimStart();
                    if (!string.IsNullOrEmpty(t))
                    {
                        if (t.StartsWith("<"))
                        {
                            // try parse XML
                            try
                            {
                                var xd = new XmlDocument();
                                xd.LoadXml(t);
                                fileType = "xml";
                            }
                            catch { }
                        }
                        else if (t.StartsWith("{") || t.StartsWith("["))
                        {
                            try
                            {
                                using var _ = JsonDocument.Parse(t);
                                fileType = "json";
                            }
                            catch { }
                        }
                    }
                }

                string sanitized;
                try
                {
                    sanitized = SanitizeEmbeddedContent(fileContent, fileType);
                }
                catch
                {
                    sanitized = string.Empty;
                }

                // escape single quotes for SQL literal
                var escaped = sanitized.Replace("'", "''");
                var replacement = "'" + escaped + "'";

                // perform replacement of the first occurrence
                var startIdx = output.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
                if (startIdx >= 0)
                {
                    var endIdx = output.IndexOf(closeTag, startIdx + openTag.Length, StringComparison.OrdinalIgnoreCase);
                    if (endIdx > startIdx)
                    {
                        var before = output.Substring(0, startIdx);
                        var after = output.Substring(endIdx + closeTag.Length);
                        output = before + replacement + after;
                    }
                    else break;
                }
                else break;
            }

            return output;
        }

        public class ParsedWorkflowInsert
        {
            public string Name { get; set; } = string.Empty;
            public int IsProduction { get; set; }
            public int IsActive { get; set; }
            public int IsValid { get; set; }
            public DateTime? CreatedAt { get; set; }
            public DateTime? UpdatedAt { get; set; }
            public int Version { get; set; } = 1;
        }
    }
}
