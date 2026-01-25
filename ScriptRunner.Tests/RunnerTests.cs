using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dnp.ScriptRunner.Tests
{
    [TestClass]
    public class RunnerTests
    {
        [TestMethod]
        public void Test_SettingsLoad_Default()
        {
            var s = Dnp.ScriptRunner.Settings.Load();
            Assert.IsNotNull(s);
        }
    }
}
