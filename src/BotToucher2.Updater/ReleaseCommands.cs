using System.Text.Json;
using BotToucher2.Update;
internal static class ReleaseCommands
{
    public static async Task CheckAsync(UpdateEngine engine)
    {
        using var http = GitHubReleaseClient.CreateHttpClient();
        var available = await new GitHubReleaseClient(http).CheckAsync(await engine.StatusAsync());
        Console.WriteLine(available is null ? "No compatible newer release is available." : JsonSerializer.Serialize(new { available.Manifest.Version, available.Manifest.RequiredBytes, available.Notes }));
    }
}
