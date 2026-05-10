using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace Go2HDR.Services;

public record UpdateResult(bool IsNewer, string LatestVersion, string ReleaseUrl);

public class UpdateService
{
    private static readonly HttpClient Http;
    private const string ApiUrl = "https://api.github.com/repos/Rayekkk/Go2HDR/releases/latest";
    private readonly SemaphoreSlim _lock = new(1, 1);

    public static readonly Version CurrentVersion =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    public UpdateResult? LastResult { get; private set; }
    public bool IsRunning { get; private set; }

    // Fired at most once per session to avoid repeat toasts on manual re-checks.
    public event Action<UpdateResult>? NewVersionFound;
    private bool _notifiedThisSession;

    static UpdateService()
    {
        Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        Http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Go2HDR", CurrentVersion.ToString(3)));
        Http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateResult?> CheckAsync()
    {
        // Skip if another check is already in progress.
        if (!await _lock.WaitAsync(0)) return LastResult;
        IsRunning = true;
        try
        {
            using var response = await Http.GetAsync(ApiUrl);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.GetProperty("tag_name").GetString() ?? "";
            var url = root.GetProperty("html_url").GetString() ?? "";
            var ver = tag.TrimStart('v', 'V');

            if (!Version.TryParse(ver, out var latest)) return null;

            // Normalise to 3 components (Major.Minor.Build) before comparing.
            // Assembly version is always 4-part (e.g. 2.1.0.0) while a GitHub
            // tag like "v2.1.0" parses to only 3 parts (Revision = -1).
            // Without normalisation, Version(2,1,0) < Version(2,1,0,0) because
            // -1 < 0, which would cause a false "older" result for the same version.
            var cur3    = new Version(CurrentVersion.Major, CurrentVersion.Minor, Math.Max(0, CurrentVersion.Build));
            var latest3 = new Version(latest.Major,         latest.Minor,         Math.Max(0, latest.Build));

            LastResult = new UpdateResult(latest3 > cur3, ver, url);
            if (LastResult.IsNewer && !_notifiedThisSession)
            {
                _notifiedThisSession = true;
                NewVersionFound?.Invoke(LastResult);
            }
            return LastResult;
        }
        catch
        {
            return null;
        }
        finally
        {
            IsRunning = false;
            _lock.Release();
        }
    }
}
