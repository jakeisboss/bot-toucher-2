using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
namespace BotToucher2.Update;

public sealed record ReleaseTransition(string? SourceId, string? SourceVersion, string ManifestAsset, string ManifestSha256, long ManifestBytes, long PayloadBytes);
public sealed record ReleaseMetadata(int Schema, string ApplicationId, string Version, string Notes, ReleaseTransition[] Transitions);
public sealed record AvailableUpdate(TransitionManifest Manifest, string Notes, IAssetSource Assets);

/// <summary>Public Releases only, one channel including prereleases. Check never opens payload assets.</summary>
public sealed class GitHubReleaseClient(HttpClient http)
{
    private const string Repository = "jakeisboss/bot-toucher-2";
    private const int MetadataLimit = 4 * 1024 * 1024;
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
    public async Task<AvailableUpdate?> CheckAsync(UpdateStatus status, CancellationToken ct = default)
    {
        if (status.Pending) throw new InvalidOperationException("Resolve the pending transaction before checking updates.");
        var candidates = new List<(Version Version, JsonElement Release)>();
        for (var page = 1; page <= 5; page++)
        {
            var bytes = await ReadBounded(new Uri($"https://api.github.com/repos/{Repository}/releases?per_page=100&page={page}"), MetadataLimit, ct);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid GitHub release response.");
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (release.GetProperty("draft").GetBoolean()) continue;
                var tag = release.GetProperty("tag_name").GetString() ?? "";
                if (!Version.TryParse(tag.TrimStart('v'), out var version)) continue;
                if (status.Current is not null && version <= Version.Parse(status.Current.Version)) continue;
                candidates.Add((version, release.Clone()));
            }
            if (doc.RootElement.GetArrayLength() < 100) break;
        }
        foreach (var candidate in candidates.OrderByDescending(c => c.Version))
        {
            var assets = new Dictionary<string, Uri>(StringComparer.Ordinal);
            foreach (var asset in candidate.Release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? throw new InvalidDataException("Asset name missing.");
                var uri = new Uri(asset.GetProperty("browser_download_url").GetString() ?? "");
                RequireAllowed(uri);
                if (uri.Host != "github.com" || !uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.Ordinal))
                    throw new InvalidDataException("Release asset is not bound to the configured repository.");
                if (!assets.TryAdd(name, uri)) throw new InvalidDataException("Duplicate asset name.");
                if (assets.Count > 16384) throw new InvalidDataException("Too many release assets.");
            }
            if (!assets.TryGetValue("bt2-release.json", out var metadataUrl)) continue;
            var metadata = JsonSerializer.Deserialize<ReleaseMetadata>(await ReadBounded(metadataUrl, 128 * 1024, ct)) ?? throw new InvalidDataException("Empty release metadata.");
            if (metadata.Schema != 1 || metadata.ApplicationId != "bot-toucher-2" || !Version.TryParse(metadata.Version, out var declaredVersion) || declaredVersion != candidate.Version || metadata.Notes is null || metadata.Notes.Length > 16384 || metadata.Transitions is null || metadata.Transitions.Length > 128)
                throw new InvalidDataException("Invalid release metadata identity or limits.");
            var transition = metadata.Transitions.SingleOrDefault(t => t.SourceId == status.Current?.InventoryId && t.SourceVersion == status.Current?.Version);
            if (transition is null) continue;
            if (transition.ManifestBytes is <= 0 or > MetadataLimit || transition.PayloadBytes < 0 || transition.ManifestSha256 is null || transition.ManifestSha256.Length != 64 || !assets.TryGetValue(transition.ManifestAsset, out var manifestUrl))
                throw new InvalidDataException("Invalid transition descriptor.");
            var manifestBytes = await ReadBounded(manifestUrl, MetadataLimit, ct);
            if (manifestBytes.LongLength != transition.ManifestBytes || !Convert.ToHexString(SHA256.HashData(manifestBytes)).Equals(transition.ManifestSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Transition manifest hash/size mismatch.");
            var manifest = JsonSerializer.Deserialize<TransitionManifest>(manifestBytes) ?? throw new InvalidDataException("Empty transition manifest.");
            if (manifest.MinimumBootstrapProtocol != 1) throw new InvalidDataException("Release requires a different bootstrap protocol; install its verified bootstrap explicitly.");
            if (manifest.Version != metadata.Version || manifest.SourceId != status.Current?.InventoryId || manifest.SourceVersion != status.Current?.Version || manifest.RequiredBytes != transition.PayloadBytes)
                throw new InvalidDataException("Transition does not match selected source/target/size.");
            if (!manifest.Target.Any(f => f.Path == "app/BotToucher2.exe") || !manifest.Target.Any(f => f.Path == "engine/BotToucher2.Updater.exe"))
                throw new InvalidDataException("Release omits an application/updater entry point.");
            // Structural validation and byte accounting only; no installation or payload access.
            var expected = UpdatePlanner.CreateManifest(manifest.Version, manifest.SourceId is null ? null : manifest.Source, manifest.Target, manifest.SourceVersion);
            if (expected.SourceId != manifest.SourceId || expected.TargetId != manifest.TargetId || expected.RequiredBytes != manifest.RequiredBytes || !expected.Operations.SequenceEqual(manifest.Operations))
                throw new InvalidDataException("Transition inventory/operation mismatch.");
            return new(manifest, metadata.Notes, new ReleaseAssets(this, assets));
        }
        return null;
    }
    private async Task<byte[]> ReadBounded(Uri uri, int maximum, CancellationToken ct)
    {
        using var response = await Send(uri, ct);
        if (response.Content.Headers.ContentLength is { } length && length > maximum) throw new InvalidDataException("Metadata exceeds byte limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var result = new MemoryStream(); var buffer = new byte[16384]; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (result.Length + read > maximum) throw new InvalidDataException("Metadata exceeds byte limit.");
            result.Write(buffer, 0, read);
        }
        return result.ToArray();
    }
    private async Task<HttpResponseMessage> Send(Uri uri, CancellationToken ct)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            RequireAllowed(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("BotToucher2/0.1");
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var next = response.Headers.Location; response.Dispose();
                if (next is null) throw new InvalidDataException("Redirect lacks a destination.");
                uri = next.IsAbsoluteUri ? next : new Uri(uri, next); continue;
            }
            if (!response.IsSuccessStatusCode) { var status = response.StatusCode; response.Dispose(); throw new HttpRequestException($"GitHub Releases request failed: {(int)status} {status}."); }
            return response;
        }
        throw new InvalidDataException("Too many release redirects.");
    }
    private static void RequireAllowed(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0) throw new InvalidDataException("Invalid release URL.");
        var allowed = uri.Host switch {
            "api.github.com" => uri.AbsolutePath.StartsWith($"/repos/{Repository}/releases", StringComparison.Ordinal),
            "github.com" => uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.Ordinal),
            "release-assets.githubusercontent.com" or "objects.githubusercontent.com" => true,
            _ => false
        };
        if (!allowed) throw new InvalidDataException("Release URL is outside the GitHub repository/CDN boundary.");
    }
    private sealed class ReleaseAssets(GitHubReleaseClient owner, Dictionary<string, Uri> assets) : IAssetSource
    {
        public async Task<Stream> OpenAsync(string assetName, CancellationToken cancellationToken)
        {
            if (!assets.TryGetValue(assetName, out var uri)) throw new InvalidDataException("Required payload asset is missing from the release.");
            var response = await owner.Send(uri, cancellationToken);
            try { return new ResponseStream(await response.Content.ReadAsStreamAsync(cancellationToken), response); }
            catch { response.Dispose(); throw; }
        }
    }
    private sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException(); public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); response.Dispose(); } base.Dispose(disposing); }
    }
}
