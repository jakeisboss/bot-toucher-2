using System.Net.Http;
using BotToucher2.Core.Activity;
using BotToucher2.Update;
namespace BotToucher2.App;

/// <summary>Manual UI frontend only. Every install/rollback mutation belongs to UpdateEngine.</summary>
public sealed class UpdateController : IDisposable
{
    private readonly UpdateEngine _engine;
    private readonly ActivityLog _log;
    private readonly HttpClient _http = GitHubReleaseClient.CreateHttpClient();
    private AvailableUpdate? _available;
    private UpdateStatus? _status;
    public Action<string, string, string>? HandoffRequested { get; set; }
    public bool CanInstall => _available is not null && _status?.Current is not null && !_status.Pending;
    public bool CanRollback => _status?.Previous is not null && !_status.Pending;
    public UpdateController(string root, ActivityLog log)
    {
        _log = log;
        _engine = new UpdateEngine(root, message => _ = LogAsync("UPDATE", message));
    }
    public async Task<string> RunAsync(string operation)
    {
        try
        {
            _status = await Task.Run(() => _engine.StatusAsync());
            if (operation == "check")
            {
                await _log.RecordAsync("UPDATE_CHECK", "Manual update check requested.");
                _available = await new GitHubReleaseClient(_http).CheckAsync(_status);
                await _log.RecordAsync("UPDATE_CHECK", _available is null ? "No compatible newer release found." : "Available version: " + _available.Manifest.Version);
            }
            else if (operation is "install" or "rollback")
            {
                if (_status.Pending) throw new InvalidOperationException("A pending transaction requires explicit external recovery.");
                if (_status.Current is null) throw new InvalidOperationException("This loose build is not a managed install. Use the external updater's install-release command for initial installation.");
                string stage = "";
                if (operation == "install")
                {
                    if (_available is null) throw new InvalidOperationException("Check and select an update first.");
                    stage = await Task.Run(() => _engine.StageAsync(_available.Manifest, _available.Assets));
                }
                else if (_status.Previous is null) throw new InvalidOperationException("No previous known-good version is available.");
                var worker = await Task.Run(() => _engine.PrepareWorkerAsync());
                await _log.RecordAsync("UPDATE_HANDOFF", "Verified external helper prepared for " + operation + ".");
                if (HandoffRequested is null) throw new InvalidOperationException("Application shutdown frontend is unavailable.");
                HandoffRequested(operation == "install" ? "apply" : "rollback", stage, worker);
                return "Staging verified. Closing BT2 for the external transaction…";
            }
            return $"Current version: {_status.Current?.Version ?? "0.1.0 (unmanaged build)"}\nLatest compatible: {_available?.Manifest.Version ?? "Not checked / no compatible release"}\nRequired payload: {(_available is null ? "Not available" : _available.Manifest.RequiredBytes + " bytes")}\nPrevious known-good: {_status.Previous?.Version ?? "None"}\n{(_status.Pending ? "Pending transaction: run external recover before proceeding." : "")}\n\n{_available?.Notes}";
        }
        catch (Exception ex)
        {
            await LogAsync("UPDATE_ERROR", ex.Message);
            throw;
        }
    }
    private async Task LogAsync(string code, string message)
    { try { await _log.RecordAsync(code, message, code.EndsWith("ERROR", StringComparison.Ordinal) ? "Error" : "Info"); } catch (Exception) { /* UI operation still reports its own result. */ } }
    public void Dispose() => _http.Dispose();
}
