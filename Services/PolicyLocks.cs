using System.Text;
using KWin.Models;

namespace KWin.Services
{
    /// <summary>Why a setting cannot be changed by K-Win.</summary>
    public enum LockReason { None, Organization, LocalGroupPolicy }

    /// <summary>
    /// Detects settings that belong to a policy K-Win must respect:
    ///  - organization management (domain join or MDM that delivered policies) for policy settings;
    ///  - local Group Policy: values listed in the local policy files
    ///    (%SystemRoot%\System32\GroupPolicy\Machine|User\Registry.pol). Windows re-applies those
    ///    at every policy refresh, so changing them directly would not stick.
    /// </summary>
    public sealed class PolicyLocks
    {
        private readonly HashSet<string> _machine;
        private readonly HashSet<string> _user;
        private readonly bool _orgManaged;

        public PolicyLocks(IEnumerable<string>? machinePolicyValues = null, IEnumerable<string>? userPolicyValues = null, bool? orgManaged = null)
        {
            _machine = new HashSet<string>(machinePolicyValues ?? ReadPol("Machine"), StringComparer.OrdinalIgnoreCase);
            _user = new HashSet<string>(userPolicyValues ?? ReadPol("User"), StringComparer.OrdinalIgnoreCase);
            _orgManaged = orgManaged ?? OrgManagement.IsManaged;
        }

        public LockReason Check(SettingDefinition d)
        {
            if (d.IsPolicy && _orgManaged) return LockReason.Organization;
            foreach (var t in d.On.Concat(d.Off))
            {
                int slash = t.Key.IndexOf('\\');
                if (slash < 0) continue;
                string root = t.Key[..slash].ToUpperInvariant();
                string id = $@"{t.Key[(slash + 1)..]}\{t.ValueName}";
                if ((root is "HKLM" or "HKEY_LOCAL_MACHINE") && _machine.Contains(id)) return LockReason.LocalGroupPolicy;
                if ((root is "HKCU" or "HKEY_CURRENT_USER") && _user.Contains(id)) return LockReason.LocalGroupPolicy;
            }
            return LockReason.None;
        }

        private static IEnumerable<string> ReadPol(string scope)
        {
            try
            {
                string path = Path.Combine(Environment.SystemDirectory, "GroupPolicy", scope, "Registry.pol");
                return File.Exists(path) ? ParseRegistryPol(File.ReadAllBytes(path)) : Array.Empty<string>();
            }
            catch
            {
                return Array.Empty<string>(); // unreadable: nothing is treated as locked
            }
        }

        /// <summary>
        /// Parses the Registry.pol format: "PReg" + version 1, then entries
        /// [key;value;type;size;data] with UTF-16 text and 32-bit type/size.
        /// Returns "key\value" for each entry ("**del." and similar markers are kept as written).
        /// </summary>
        public static List<string> ParseRegistryPol(byte[] data)
        {
            var result = new List<string>();
            if (data.Length < 8 || Encoding.ASCII.GetString(data, 0, 4) != "PReg") return result;

            int i = 8;
            while (i + 2 <= data.Length)
            {
                if (ReadChar(data, i) != '[') break;
                i += 2;
                string key = ReadString(data, ref i);
                string value = ReadString(data, ref i);
                if (i + 4 + 2 + 4 + 2 > data.Length) break;
                i += 4;          // type
                i += 2;          // ';'
                int size = BitConverter.ToInt32(data, i);
                i += 4 + 2;      // size + ';'
                if (size < 0 || i + size > data.Length) break;
                i += size;       // data
                if (i + 2 > data.Length || ReadChar(data, i) != ']') break;
                i += 2;
                result.Add($@"{key}\{value}");
            }
            return result;
        }

        private static char ReadChar(byte[] d, int i) => (char)(d[i] | (d[i + 1] << 8));

        /// <summary>Reads a null-terminated UTF-16 string followed by ';'.</summary>
        private static string ReadString(byte[] d, ref int i)
        {
            var sb = new StringBuilder();
            while (i + 2 <= d.Length)
            {
                char c = ReadChar(d, i);
                i += 2;
                if (c == '\0') break;
                sb.Append(c);
            }
            if (i + 2 <= d.Length && ReadChar(d, i) == ';') i += 2;
            return sb.ToString();
        }
    }
}
