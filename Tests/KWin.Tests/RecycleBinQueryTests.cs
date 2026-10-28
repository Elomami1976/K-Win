using KWin.Core;
using Xunit;

namespace KWin.Tests
{
    public class RecycleBinQueryTests
    {
        /// <summary>Read-only: asks Windows for the Recycle Bin size (1.0 always failed on x64).</summary>
        [Fact]
        public void RecycleBinSize_CanBeQueried()
        {
            var r = CleanupManager.GetRecycleBinSize();
            Assert.True(r.Success, r.ErrorMessage);
            Assert.True(r.Data.SizeBytes >= 0);
            Assert.True(r.Data.ItemCount >= 0);
        }
    }
}
