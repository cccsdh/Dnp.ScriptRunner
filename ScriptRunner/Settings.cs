/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using System;
using System.Linq;
using System.Text.Json;

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
}
