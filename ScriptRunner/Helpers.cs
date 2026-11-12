/*
 * Copyright (c) 2026 Doughnuts Publishing
 * All rights reserved.
 *
 * Author: Doughnuts Publishing
 * Licensed under the MIT License. See LICENSE in project root for license details.
 */

using Spectre.Console;
using System;
using System.Linq;
using System.Text;

namespace Dnp.ScriptRunner
{
    static class Helpers
    {
        // Renders the ScriptRunner banner and copyright footer at the top of each menu screen
        public static void RenderHeader()
        {
            // The figlet banner needs ~86 columns to avoid wrapping; fall back to plain text on narrow terminals
            if (AnsiConsole.Profile.Width >= 90)
                AnsiConsole.Write(new FigletText("ScriptRunner").Centered().Color(Color.Yellow));
            else
                AnsiConsole.Write(new Rule("[bold yellow]ScriptRunner[/]") { Justification = Justify.Center });

            var year = DateTime.Now.Year;
            var rule = new Rule($"[dim]© {year} Doughnuts Publishing[/]") { Justification = Justify.Center };
            AnsiConsole.Write(rule);
            // add a separator line so subsequent output appears below the header
            AnsiConsole.WriteLine();
        }

        // Prompts for a directory, offering previously saved directories for the given provider or letting the user enter a new one.
        // When createIfMissing is true, the directory need not already exist and is created on disk before returning.
        public static string PromptForDirectory(Settings settings, string dbType, string selectionTitle, string textPromptLabel, bool createIfMissing = false)
        {
            const string newDirectoryLabel = "<New directory...>";
            var savedDirs = settings.GetDirectories(dbType);
            string dir;

            if (savedDirs.Count > 0)
            {
                var dirChoices = new List<string>(savedDirs) { newDirectoryLabel };
                var pickDir = AnsiConsole.Prompt(
                    new SelectionPrompt<string>().Title(selectionTitle).AddChoices(dirChoices));

                if (pickDir == newDirectoryLabel)
                {
                    dir = PromptForNewDirectory(textPromptLabel, createIfMissing);
                    settings.AddDirectory(dbType, dir);
                    settings.Save();
                }
                else
                {
                    dir = pickDir;
                }
            }
            else
            {
                dir = PromptForNewDirectory(textPromptLabel, createIfMissing);
                settings.AddDirectory(dbType, dir);
                settings.Save();
            }

            if (createIfMissing && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            return dir;
        }

        private static string PromptForNewDirectory(string textPromptLabel, bool createIfMissing = false)
        {
            var prompt = new TextPrompt<string>(textPromptLabel)
                .DefaultValue(Environment.CurrentDirectory);

            if (!createIfMissing)
            {
                prompt.Validate(s => Directory.Exists(s) ? ValidationResult.Success() : ValidationResult.Error("[red]Directory not found[/]"));
            }

            return AnsiConsole.Prompt(prompt);
        }
    }
}
