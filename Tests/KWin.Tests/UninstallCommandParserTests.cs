using KWin.Core;
using Xunit;

namespace KWin.Tests
{
    public class UninstallCommandParserTests
    {
        [Fact]
        public void QuotedPath_WithArguments_IsSplitCorrectly()
        {
            var r = UninstallCommandParser.Parse("\"C:\\Program Files\\App\\unins000.exe\" /SILENT");
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal(@"C:\Program Files\App\unins000.exe", r.Data!.FileName);
            Assert.Equal("/SILENT", r.Data.Arguments);
            Assert.False(r.Data.IsMsi);
        }

        [Fact]
        public void UnquotedPath_WithSpaces_IsSplitAtExe()
        {
            var r = UninstallCommandParser.Parse(@"C:\Program Files\My App\uninstall.exe --remove now");
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal(@"C:\Program Files\My App\uninstall.exe", r.Data!.FileName);
            Assert.Equal("--remove now", r.Data.Arguments);
        }

        [Fact]
        public void Msi_UsesOnlyTheProductCode()
        {
            var r = UninstallCommandParser.Parse("MsiExec.exe /I{12345678-1234-1234-1234-123456789ABC} & calc.exe");
            Assert.True(r.Success, r.ErrorMessage);
            Assert.True(r.Data!.IsMsi);
            Assert.EndsWith("msiexec.exe", r.Data.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.True(Path.IsPathFullyQualified(r.Data.FileName));
            Assert.Equal("/x {12345678-1234-1234-1234-123456789ABC} /passive /norestart", r.Data.Arguments);
            Assert.DoesNotContain("calc", r.Data.Arguments);
        }

        [Fact]
        public void Msi_WithoutProductCode_IsRefused()
        {
            Assert.False(UninstallCommandParser.Parse("msiexec.exe /x something").Success);
        }

        [Theory]
        [InlineData("cmd.exe /c del C:\\important")]
        [InlineData("C:\\Windows\\System32\\cmd.exe /c calc")]
        [InlineData("\"C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -Command evil")]
        [InlineData("C:\\Windows\\System32\\rundll32.exe shell32.dll,Control_RunDLL")]
        [InlineData("C:\\Windows\\System32\\mshta.exe http://example.invalid")]
        [InlineData("C:\\Windows\\System32\\wscript.exe C:\\x.vbs")]
        public void ShellsAndScriptHosts_AreRefused(string uninstall)
        {
            var r = UninstallCommandParser.Parse(uninstall);
            Assert.False(r.Success);
        }

        [Theory]
        [InlineData("uninstall.exe /S")]            // relative path
        [InlineData("..\\app\\uninstall.exe")]      // relative path
        [InlineData("C:\\App\\uninstall.bat")]      // not an .exe
        [InlineData("\"C:\\App\\uninstall.exe")]    // unmatched quote
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("C:\\App\\un\ninstall.exe")]    // control character
        public void InvalidCommands_AreRefused(string uninstall)
        {
            Assert.False(UninstallCommandParser.Parse(uninstall).Success);
        }

        [Fact]
        public void ShellOperators_StayLiteralArguments_NotCommands()
        {
            // Without a shell, "&" is just text passed to the uninstaller; no second program is started.
            var r = UninstallCommandParser.Parse("\"C:\\App\\uninstall.exe\" /S & calc.exe");
            Assert.True(r.Success, r.ErrorMessage);
            Assert.Equal(@"C:\App\uninstall.exe", r.Data!.FileName);
            Assert.Equal("/S & calc.exe", r.Data.Arguments);
        }

        [Fact]
        public void EnvironmentVariables_AreExpanded()
        {
            var r = UninstallCommandParser.Parse("\"%ProgramFiles%\\App\\uninstall.exe\" /S");
            Assert.True(r.Success, r.ErrorMessage);
            Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), r.Data!.FileName,
                StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("Microsoft.BingNews_4.55.62231.0_x64__8wekyb3d8bbwe", true)]
        [InlineData("Clipchamp.Clipchamp", true)]
        [InlineData("Evil'; Remove-Item C:\\ -Recurse; '", false)]
        [InlineData("name with spaces", false)]
        [InlineData("$(calc)", false)]
        [InlineData("", false)]
        public void PackageNames_AreValidatedBeforePowerShell(string name, bool expected)
        {
            Assert.Equal(expected, AppManager.IsValidPackageName(name));
        }
    }
}
