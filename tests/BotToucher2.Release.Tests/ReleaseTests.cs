using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BotToucher2.Update;
using Xunit;
namespace BotToucher2.Release.Tests;
public sealed class ReleaseTests
{
    [Fact] public async Task Check_fetches_only_small_metadata_and_manifest_not_payload_even_for_large_install()
    {
        var bytes = Encoding.UTF8.GetBytes("changed");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var baseline = new[] { new InventoryFile("app/runtime.dll", 200_000_000, new string('b', 64)) };
        var manifest = UpdatePlanner.CreateManifest("0.1.1", baseline, baseline.Concat(new[] { new InventoryFile("app/BotToucher2.exe", bytes.Length, hash) }), "0.1.0");
        var handler = new FixtureHandler(manifest);
        var client = new GitHubReleaseClient(new HttpClient(handler));
        var result = await client.CheckAsync(new UpdateStatus(new InstallationIdentity("0.1.0", manifest.SourceId!), null, "/must-not-be-scanned", null, false));
        Assert.NotNull(result);
        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, p => p.EndsWith(hash, StringComparison.Ordinal));
        Assert.Equal(7, result!.Manifest.RequiredBytes);
        await using var payload = await result.Assets.OpenAsync(hash, CancellationToken.None);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("changed", await new StreamReader(payload).ReadToEndAsync());
    }
    [Fact] public async Task Check_rejects_manifest_hash_mismatch_before_install()
    {
        var manifest = UpdatePlanner.CreateManifest("0.1.1", null, new[] { new InventoryFile("app/BotToucher2.exe", 0, new string('a',64)) });
        var handler = new FixtureHandler(manifest) { CorruptManifest = true };
        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubReleaseClient(new HttpClient(handler)).CheckAsync(new(null,null,null,null,false)));
        Assert.Equal(3, handler.Requests.Count);
    }
    private sealed class FixtureHandler(TransitionManifest manifest) : HttpMessageHandler
    {
        public bool CorruptManifest { get; init; }
        public List<string> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsoluteUri; Requests.Add(path);
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
            var baseUrl = "https://github.com/jakeisboss/bot-toucher-2/releases/download/v0.1.1/";
            var metadata = new ReleaseMetadata(1,"bot-toucher-2","0.1.1","Real release notes",new[] {
                new ReleaseTransition(manifest.SourceId,manifest.SourceVersion,"transition.json",Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),manifestBytes.Length,manifest.RequiredBytes) });
            object asset(string name) => new { name, browser_download_url = baseUrl + name };
            byte[] body = path.StartsWith("https://api.github.com/",StringComparison.Ordinal) ? JsonSerializer.SerializeToUtf8Bytes(new[] {
                new { tag_name="v0.1.1", draft=false, prerelease=true, assets=new[] {asset("bt2-release.json"),asset("transition.json"),asset(manifest.Operations.First(o => o.AssetName is not null).AssetName!)} } }) :
                path.EndsWith("bt2-release.json",StringComparison.Ordinal) ? JsonSerializer.SerializeToUtf8Bytes(metadata) :
                path.EndsWith("transition.json",StringComparison.Ordinal) ? (CorruptManifest ? Encoding.UTF8.GetBytes("{}") : manifestBytes) : Encoding.UTF8.GetBytes("changed");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }
}
