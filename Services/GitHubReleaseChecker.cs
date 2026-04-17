using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TempestData.Services
{
    public sealed class GitHubReleaseChecker
    {
        private readonly HttpClient _httpClient;
        private readonly string _owner;
        private readonly string _repo;

        public GitHubReleaseChecker(HttpClient httpClient, string owner = "MABeatty1978", string repo = "TempestData")
        {
            _httpClient = httpClient;
            _owner = owner;
            _repo = repo;
        }

        public async Task<ReleaseCheckResult> CheckForUpdatesAsync(string currentVersion, CancellationToken cancellationToken = default)
        {
            if (!TryParseVersion(currentVersion, out var current))
            {
                return ReleaseCheckResult.Failure($"Invalid current app version '{currentVersion}'.");
            }

            var endpoint = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.UserAgent.ParseAdd("TempestData");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return ReleaseCheckResult.Failure($"GitHub release check failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var rawTag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var releaseUrl = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
            var isPrerelease = root.TryGetProperty("prerelease", out var preElement) && preElement.ValueKind == JsonValueKind.True;

            if (isPrerelease)
            {
                return new ReleaseCheckResult(true, false, current.ToString(), rawTag ?? "unknown", releaseUrl ?? string.Empty, true, null);
            }

            if (string.IsNullOrWhiteSpace(rawTag) || !TryParseVersion(rawTag, out var latest))
            {
                return ReleaseCheckResult.Failure("Could not parse the latest release tag from GitHub.");
            }

            var updateAvailable = latest > current;
            return new ReleaseCheckResult(
                true,
                updateAvailable,
                current.ToString(),
                latest.ToString(),
                releaseUrl ?? $"https://github.com/{_owner}/{_repo}/releases/latest",
                false,
                null);
        }

        private static bool TryParseVersion(string input, out Version version)
        {
            version = new Version(0, 0);
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var normalized = input.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[1..];
            }

            var preIndex = normalized.IndexOf('-');
            if (preIndex >= 0)
            {
                normalized = normalized[..preIndex];
            }

            var buildIndex = normalized.IndexOf('+');
            if (buildIndex >= 0)
            {
                normalized = normalized[..buildIndex];
            }

            if (!Version.TryParse(normalized, out var parsed) || parsed == null)
            {
                return false;
            }

            version = parsed;
            return true;
        }
    }

    public sealed record ReleaseCheckResult(
        bool IsSuccess,
        bool IsUpdateAvailable,
        string CurrentVersion,
        string LatestVersion,
        string ReleaseUrl,
        bool IsPrerelease,
        string? ErrorMessage)
    {
        public static ReleaseCheckResult Failure(string errorMessage)
        {
            return new ReleaseCheckResult(false, false, string.Empty, string.Empty, string.Empty, false, errorMessage);
        }
    }
}
