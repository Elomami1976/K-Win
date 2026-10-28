using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace KWin.Services
{
    /// <summary>
    /// Detects whether this PC is managed by an organization (joined to an Active Directory domain,
    /// or enrolled in device management such as Intune). On managed PCs, Group Policy settings belong
    /// to the organization: K-Win shows them read-only and does not change them.
    /// </summary>
    public static class OrgManagement
    {
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetGetJoinInformation(string? server, out IntPtr domain, out int status);

        [DllImport("netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);

        private const int NetSetupDomainName = 3;

        private static readonly Lazy<bool> _domain = new(() =>
        {
            try
            {
                if (NetGetJoinInformation(null, out var name, out int status) != 0) return false;
                NetApiBufferFree(name);
                return status == NetSetupDomainName;
            }
            catch
            {
                return false;
            }
        });

        private static readonly Lazy<bool> _mdm = new(() =>
        {
            try
            {
                using var enrollments = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
                if (enrollments == null) return false;
                foreach (var name in enrollments.GetSubKeyNames())
                {
                    using var e = enrollments.OpenSubKey(name);
                    if (e?.GetValue("ProviderID") is not string provider || provider.Length == 0) continue;

                    // Adding a work or school account also creates an enrollment, but that alone
                    // does not manage the PC. It counts only once the provider has delivered
                    // device policies (PolicyManager\Providers\{enrollment id}).
                    using var policies = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\PolicyManager\Providers\{name}");
                    if (policies != null && policies.SubKeyCount > 0) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        });

        public static bool IsDomainJoined => _domain.Value;
        public static bool IsMdmEnrolled => _mdm.Value;
        public static bool IsManaged => IsDomainJoined || IsMdmEnrolled;
    }
}
