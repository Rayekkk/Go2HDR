using System.Net.Http;
using System.Net.Http.Headers;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;

namespace Go2HDR.Services;

public record UpdateResult(bool IsNewer, string LatestVersion, Uri ReleaseUri);

public sealed class UpdateService : IDisposable
{
    public static readonly Version CurrentVersion =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly Uri ApiUrl = new("https://api.github.com/repos/Rayekkk/Go2HDR/releases/latest");
    private readonly object _sync = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task<UpdateResult?>? _inFlight;
    private bool _disposed;
    private volatile bool _isRunning;

    public UpdateResult? LastResult { get; private set; }
    public bool IsRunning => _isRunning;

    // Fired at most once per session to avoid repeat toasts on manual re-checks.
    public event Action<UpdateResult>? NewVersionFound;
    public event Action<UpdateResult?>? CheckCompleted;
    private bool _notifiedThisSession;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Go2HDR", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    public Task<UpdateResult?> CheckAsync()
    {
        lock (_sync)
        {
            if (_disposed) return Task.FromResult<UpdateResult?>(null);
            if (_inFlight is { IsCompleted: false }) return _inFlight;
            _inFlight = CheckCoreAsync(_shutdown.Token);
            return _inFlight;
        }
    }

    private async Task<UpdateResult?> CheckCoreAsync(CancellationToken cancellationToken)
    {
        _isRunning = true;
        UpdateResult? result = null;
        try
        {
            using var response = await Http.GetAsync(
                ApiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!TryCreateResult(doc.RootElement, CurrentVersion, out result)) return null;

            LastResult = result;
            if (LastResult.IsNewer && !_notifiedThisSession)
            {
                _notifiedThisSession = true;
                NewVersionFound?.Invoke(LastResult);
            }
            return LastResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Write("Checking for updates failed.", ex);
            return null;
        }
        finally
        {
            _isRunning = false;
            try
            {
                CheckCompleted?.Invoke(result);
            }
            catch (Exception ex)
            {
                AppLog.Write("Handling the completed update check failed.", ex);
            }
        }
    }

    internal static bool TryCreateResult(
        JsonElement root, Version currentVersion, [NotNullWhen(true)] out UpdateResult? result)
    {
        result = null;
        if (!root.TryGetProperty("tag_name", out JsonElement tagElement) ||
            !root.TryGetProperty("html_url", out JsonElement urlElement))
            return false;

        string tag = tagElement.GetString() ?? "";
        string url = urlElement.GetString() ?? "";
        string versionText = tag.Length > 0 && tag[0] is 'v' or 'V' ? tag[1..] : tag;

        if (!Version.TryParse(versionText, out Version? latest) ||
            !TryGetTrustedReleaseUri(url, out Uri releaseUri))
            return false;

        result = new UpdateResult(
            NormalizeVersion(latest) > NormalizeVersion(currentVersion),
            versionText,
            releaseUri);
        return true;
    }

    internal static Version NormalizeVersion(Version version) => new(
        version.Major,
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));

    internal static bool TryGetTrustedReleaseUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps &&
            parsed.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            parsed.IsDefaultPort &&
            string.IsNullOrEmpty(parsed.UserInfo) &&
            parsed.AbsolutePath.StartsWith("/Rayekkk/Go2HDR/releases/", StringComparison.OrdinalIgnoreCase))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _shutdown.Cancel();
            _shutdown.Dispose();
        }
    }
}
