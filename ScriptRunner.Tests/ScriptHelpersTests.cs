using Microsoft.VisualStudio.TestTools.UnitTesting;
using Dnp.ScriptRunner;
using System.IO;
using System.Text.Json;
using System;

namespace Dnp.ScriptRunner.Tests
{
    [TestClass]
    public class ScriptHelpersTests
    {
        [TestMethod]
        public void ParseWorkflowInsert_SimpleEmbeddedFile_Works()
        {
            var sql = "INSERT INTO Workflows (Name, JsonDefinition, IsProduction, IsActive, IsValid, CreatedAt, UpdatedAt, Version) VALUES ('TestWorkflow', <DnPJson>test.json</DnPJson>, 1, 1, 1, NOW(), NOW(), 2);";
            var parsed = ScriptHelpers.ParseWorkflowInsert(sql);
            Assert.IsNotNull(parsed);
            Assert.AreEqual("TestWorkflow", parsed.Name);
            Assert.AreEqual(1, parsed.IsProduction);
            Assert.AreEqual(1, parsed.IsActive);
            Assert.AreEqual(1, parsed.IsValid);
            Assert.AreEqual(2, parsed.Version);
        }

        [TestMethod]
        public void SanitizeEmbeddedContent_Json_Normalizes()
        {
            var json = "{\n  \"Name\": \"X\", \n  \"Value\": 1\n}";
            var sanitized = ScriptHelpers.SanitizeEmbeddedContent(json, "json");
            using var doc = JsonDocument.Parse(sanitized);
            Assert.IsTrue(doc.RootElement.TryGetProperty("Name", out var _));
        }

        [TestMethod]
        [ExpectedException(typeof(System.Xml.XmlException))]
        public void SanitizeEmbeddedContent_Xml_Invalid_Throws()
        {
            var xml = "<root><unclosed></root>";
            ScriptHelpers.SanitizeEmbeddedContent(xml, "xml");
        }

        [TestMethod]
        public void TryExtractEmbeddedFileTag_FindsDefaultFileTag()
        {
            var sql = "... <file>path/to/file.json</file> ...";
            var markers = ScriptHelpers.DefaultMarkers;
            var ok = ScriptHelpers.TryExtractEmbeddedFileTag(sql, markers, out var open, out var close, out var rel, out var type);
            Assert.IsTrue(ok);
            Assert.AreEqual("<file>", open);
            Assert.AreEqual("</file>", close);
            Assert.AreEqual("txt", type);
            Assert.AreEqual("path/to/file.json", rel);
        }

        [TestMethod]
        public void ReplaceEmbeddedTags_RelativePathAndDetection_Works()
        {
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                // create languages folder and file
                var langDir = Path.Combine(tmp, "languages");
                Directory.CreateDirectory(langDir);
                var china = Path.Combine(langDir, "China.json");
                File.WriteAllText(china, "[{ \"Language\": \"Mandarin\" }]");

                // create sql that references ../languages/China.json from within a scripts folder
                var scriptsDir = Path.Combine(tmp, "scripts");
                Directory.CreateDirectory(scriptsDir);
                var sql = "INSERT INTO Countries (Name, Capital, Languages, Population) VALUES ('China','Beijing', <DnPTxt>../languages/China.json</DnPTxt>, 1402112000);";

                var replaced = ScriptHelpers.ReplaceEmbeddedTags(sql, scriptsDir, ScriptHelpers.DefaultMarkers, enableDetection: true);
                Assert.IsFalse(replaced.Contains("<DnPTxt>"));
                Assert.IsTrue(replaced.Contains("'"));
                Assert.IsTrue(replaced.Contains("Mandarin"));
            }
            finally
            {
                Directory.Delete(tmp, true);
            }
        }

        [TestMethod]
        public void ReplaceEmbeddedTags_XmlDetection_Works()
        {
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                var scriptsDir = tmp;
                var xmlFile = Path.Combine(tmp, "test.xml");
                File.WriteAllText(xmlFile, "<root><a>1</a></root>");
                var sql = "INSERT INTO TableX (Col) VALUES (<DnPTxt>test.xml</DnPTxt>);";
                var replaced = ScriptHelpers.ReplaceEmbeddedTags(sql, scriptsDir, ScriptHelpers.DefaultMarkers, enableDetection: true);
                Assert.IsFalse(replaced.Contains("<DnPTxt>"));
                Assert.IsTrue(replaced.Contains("<root"));
            }
            finally
            {
                Directory.Delete(tmp, true);
            }
        }

        [TestMethod]
        public void ReplaceEmbeddedTags_DetectionDisabled_TreatsAsText()
        {
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tmp);
            try
            {
                var xmlFile = Path.Combine(tmp, "test.xml");
                File.WriteAllText(xmlFile, "<root><a>1</a></root>");
                var sql = "INSERT INTO TableX (Col) VALUES (<DnPTxt>test.xml</DnPTxt>);";
                var replaced = ScriptHelpers.ReplaceEmbeddedTags(sql, tmp, ScriptHelpers.DefaultMarkers, enableDetection: false);
                // Detection disabled -> should be treated as plain text -> sanitized is trimmed version of XML but not parsed, we expect escaped '<' to remain
                Assert.IsFalse(replaced.Contains("<DnPTxt>"));
                Assert.IsTrue(replaced.Contains("&lt;") == false); // we don't HTML-escape; keep raw but quoted
                Assert.IsTrue(replaced.Contains("<root"));
            }
            finally
            {
                Directory.Delete(tmp, true);
            }
        }

        [TestMethod]
        public void SettingsLoad_DefaultEnableDetection_True()
        {
            var s = Settings.Load();
            // default should be true unless user settings override
            Assert.IsTrue(s.EnableEmbeddedTypeDetection);
        }
    }
}
