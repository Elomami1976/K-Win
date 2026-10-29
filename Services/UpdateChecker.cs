using System.Net.Http.Headers;
using System.Text.Json;
using KWin.Models;
using KWin.Utils;

namespace KWin.Services
{
    /// <summary>Latest published release, as reported by GitHub.</summary>
    public sealed class ReleaseInfo
    {
        public required string Tag { get; init; }
        public required Version Version { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Notes { get; init; } = string.Empty;
        public required Uri PageUrl { get; init; }
        public DateTime? PublishedUtc { get; init; }
    }

    public sealed class UpdateCheckResult
    {
        public bool UpdateAvailable { get; init; }
        public required Version Current { get; init; }
        public ReleaseInfo? Latest { get; init; }
    }

    /// <summary>
    /// Optional update check against the official repository (off by default; only runs when the
    /// user enables it or clicks "Check now"). It only READS release metadata from the GitHub API.
    /// It never downloads or runs anything: the user is offered the official release page.
    /// </summary>
    public sealed class UpdateChecker
    {
        public const string Owner = "Elomami1976";
        public const string Repo = "K-Win";
        public static readonly Uri LatestReleaseApi = new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

        private readonly HttpMessageHandler? _handler;

        /// <param name="handler">Injected in tests; null = real network.</param>
        public UpdateChecker(HttpMessageHandler? handler = null) => _handler = handler;

        public async Task<Result<UpdateCheckResult>> CheckAsync(Version current, CancellationToken ct = default)
        {
            try
            {
                using var http = _handler == null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
                http.Timeout = TimeSpan.FromSeconds(15);
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("K-win", current.ToString()));
                http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                using var response = await http.GetAsync(LatestReleaseApi, ct).ConfigureAwait(false);
                if ((int)response.StatusCode == 404)
                    return Result<UpdateCheckResult>.Fail("No published release was found.");
                if (!response.IsSuccessStatusCode)
                    return Result<UpdateCheckResult>.Fail($"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.");

                string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var latest = ParseRelease(json);
                if (latest == null)
                    return Result<UpdateCheckResult>.Fail("The release information could not be read.");

                Logger.Instance.Info("UpdateCheck", LatestReleaseApi.ToString(), "success",
                    oldValue: current.ToString(), newValue: latest.Version.ToString());
                return Result<UpdateCheckResult>.Ok(new UpdateCheckResult
                {
                    Current = current,
                    Latest = latest,
                    UpdateAvailable = latest.Version > Normalize(current)
                });
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return Result<UpdateCheckResult>.Fail("The connection timed out. Check your internet connection.");
            }
            catch (HttpRequestException ex)
            {
                return Result<UpdateCheckResult>.Fail($"No connection to GitHub ({ex.Message}). K-win works fully offline.");
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("UpdateCheck", LatestReleaseApi.ToString(), ex);
                return Result<UpdateCheckResult>.Fail(ex);
            }
        }

        /// <summary>
        /// Reads tag, name, notes, page and date. Returns null for drafts, unparsable versions or a
        /// page that is not this repository's release page on github.com.
        /// </summary>
        public static ReleaseInfo? ParseRelease(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
                if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;

                string tag = root.GetProperty("tag_name").GetString() ?? "";
                var version = ParseVersion(tag);
                if (version == null) return null;

                string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
                if (!IsOfficialReleasePage(url)) return null;

                DateTime? published = null;
                if (root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(p.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
                    published = dt;

                return new ReleaseInfo
                {
                    Tag = tag,
                    Version = version,
                    Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag,
                    Notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                    PageUrl = new Uri(url),
                    PublishedUtc = published
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Only https://github.com/Elomami1976/K-Win/releases/... is ever opened.</summary>
        public static bool IsOfficialReleasePage(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps &&
            uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            uri.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// "v1.5.0", "1.5", "K-Win-1.5.2" -> 1.5.0.0 / 1.5.0.0 / 1.5.2.0 (missing parts are 0, so
        /// "1.5" and "1.5.0" are equal). Returns null when the tag holds no version number.
        /// </summary>
        public static Version? ParseVersion(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(tag, @"(\d+)\.(\d+)(?:\.(\d+))?(?:\.(\d+))?");
            if (!m.Success) return null;
            int Part(int g) => m.Groups[g].Success ? int.Parse(m.Groups[g].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            return new Version(Part(1), Part(2), Part(3), Part(4));
        }

        /// <summary>Makes 1.5 and 1.5.0.0 compare equal.</summary>
        public static Version Normalize(Version v) =>
            new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));
    }
}
