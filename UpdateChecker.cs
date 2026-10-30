using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Branvik.EmulatorUpdater
{
    public static class UpdateChecker
    {
        public sealed class Release
        {
            public string tag_name { get; set; }
            public string published_at { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
            public List<Asset> assets { get; set; }
        }
        public sealed class Asset
        {
            public string name { get; set; }
            public string browser_download_url { get; set; }
            public string digest { get; set; }
            public long size { get; set; }
        }
        public static Release SelectNewest(IEnumerable<Release> releases)
        {
            return releases.Where(r => r != null && !r.draft && !string.IsNullOrWhiteSpace(r.tag_name))
                .OrderByDescending(r => PublicationDate(r.published_at)).FirstOrDefault();
        }
        private static DateTimeOffset PublicationDate(string value)
        {
            DateTimeOffset date;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date)
                ? date : DateTimeOffset.MinValue;
        }
        public static bool ValidRepository(string value) => Regex.IsMatch(value ?? "", @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$");
        public static string Compare(string installed, string latest)
        {
            if (string.IsNullOrWhiteSpace(installed)) return "Latest release: " + latest + "; installed version unknown";
            if (string.Equals(installed.Trim(), latest, StringComparison.OrdinalIgnoreCase)) return "This release is installed: " + latest;
            Version a, b;
            if (Version.TryParse(installed.Trim().TrimStart('v', 'V'), out a) && Version.TryParse(latest.TrimStart('v', 'V'), out b))
                return b > a ? "Update available: " + latest : "No newer release: " + latest;
            return "Tags differ; compare manually: " + latest;
        }
        public static async Task Check(Installation item, CancellationToken cancellation)
        {
            item.ReleaseUrl = null;
            item.CheckedRelease = null;
            if (!ValidRepository(item.Repository)) { item.Status = "Enter a GitHub repository: owner/repo"; return; }
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("BranvikEmulatorUpdater/0.1.0");
                var endpoint = item.IncludePrereleases ? "/releases?per_page=100" : "/releases/latest";
                using (var response = await client.GetAsync("https://api.github.com/repos/" + item.Repository + endpoint, cancellation))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        item.Status = response.StatusCode == HttpStatusCode.NotFound ? "Repository or release for the selected channel not found" : "GitHub HTTP " + (int)response.StatusCode + " (the API rate limit may have been reached)";
                        return;
                    }
                    var serializer = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
                    var json = await response.Content.ReadAsStringAsync();
                    var release = item.IncludePrereleases
                        ? SelectNewest(serializer.Deserialize<List<Release>>(json) ?? new List<Release>())
                        : serializer.Deserialize<Release>(json);
                    if (release == null || release.draft || string.IsNullOrWhiteSpace(release.tag_name))
                    { item.Status = "No published releases found"; return; }
                    var tag = release.tag_name;
                    item.CheckedRelease = release;
                    item.ReleaseUrl = "https://github.com/" + item.Repository + "/releases/tag/" + Uri.EscapeDataString(tag);
                    item.Status = (release.prerelease ? "[Prerelease] " : "[Stable] ") + Compare(item.InstalledTag, tag);
                }
            }
        }
    }
}
