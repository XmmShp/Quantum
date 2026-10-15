using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Quantum.Platform.Contract;

namespace Quantum.Platform.UI.Components.Pages;

public partial class Workspace
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private bool _initialized;
    private bool _busy;
    private bool _registering;
    private bool _resettingPassword;
    private string _confirmPassword = string.Empty;
    private bool _noticeError;
    private bool _editing;
    private bool _confirmedPrivateKey;
    private string _view = "plugins";
    private string? _token;
    private string? _notice;
    private string _username = string.Empty;
    private string _email = string.Empty;
    private string _verificationCode = string.Empty;
    private string _password = string.Empty;
    private string _pluginId = string.Empty;
    private string _pluginName = string.Empty;
    private string _pluginDescription = string.Empty;
    private string _pluginTags = string.Empty;
    private string _releaseVersion = string.Empty;
    private string _versionSupport = ">=0.1.0";
    private string _releaseNotes = string.Empty;
    private string? _archiveName;
    private IBrowserFile? _archive;
    private string _clientId = string.Empty;
    private string _clientName = string.Empty;
    private UserSummary? _user;
    private PluginSummary? _selected;
    private PluginSummary[] _plugins = [];
    private PluginReleaseSummary[] _releases = [];
    private PluginReleaseSummary[] _reviews = [];
    private CredentialClientSummary[] _credentials = [];
    private CreateCredentialClientResponse? _generatedCredential;
    private readonly HashSet<string> _allowedPlugins = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _reviewNotes = new(StringComparer.Ordinal);

    private bool CanReview => _user is not null &&
        (_user.Roles & (PlatformUserRoles.Reviewer | PlatformUserRoles.Admin)) != 0;
    private int _releaseTotal => _plugins.Count(plugin => plugin.LatestRelease is not null);
    private int _pendingTotal => _releases.Count(release => release.Status == PluginReleaseState.Pending);
    private string PluginsNavClass => NavClass("plugins");
    private string CredentialsNavClass => NavClass("credentials");
    private string ReviewNavClass => NavClass("review");

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _token = await JS.InvokeAsync<string?>("quantumWorkspace.getSessionToken");
        if (!string.IsNullOrWhiteSpace(_token))
        {
            try
            {
                _user = await RpcAsync<UserSummary>("GetCurrentUser", new { });
                await LoadPluginsAsync();
            }
            catch
            {
                await ClearSessionAsync();
            }
        }

        _initialized = true;
        StateHasChanged();
    }

    private void SetAuthMode(bool registering)
    {
        _registering = registering;
        _resettingPassword = false;
        _password = _confirmPassword = _verificationCode = string.Empty;
        _notice = null;
    }

    private void BeginPasswordReset()
    {
        SetAuthMode(false);
        _resettingPassword = true;
    }

    private async Task AuthenticateAsync()
    {
        await RunAsync(async () =>
        {
            if (_resettingPassword)
            {
                if (_password != _confirmPassword)
                {
                    throw new InvalidOperationException("两次输入的新密码不一致。");
                }
                await RpcAsync("ResetPassword", new
                {
                    email = _email,
                    verificationCode = _verificationCode,
                    newPassword = _password
                }, authenticated: false);
                SetAuthMode(false);
                Notify("密码已重置，请使用新密码登录。");
                return;
            }
            if (_registering)
            {
                await RpcAsync("RegisterUser", new
                {
                    username = _username,
                    email = _email,
                    password = _password,
                    verificationCode = _verificationCode
                }, authenticated: false);
            }

            var login = await RpcAsync<LoginResponse>("Login", new { email = _email, password = _password }, authenticated: false);
            _token = login.AccessToken;
            _user = login.User;
            _password = string.Empty;
            await JS.InvokeVoidAsync("quantumWorkspace.setSessionToken", _token);
            await LoadPluginsAsync();
            Notify("登录成功。");
        });
    }

    private Task SendVerificationCodeAsync() => RunAsync(async () =>
    {
        await RpcAsync(_resettingPassword ? "RequestPasswordResetEmailCode" : "RequestRegistrationEmailCode",
            new { email = _email }, authenticated: false);
        Notify(_resettingPassword
            ? "若该邮箱已注册，将收到找回密码验证码；请检查邮箱（包括垃圾邮件），60 秒后可重试。"
            : "验证码已发送，有效期 10 分钟。");
    });

    private async Task LogoutAsync()
    {
        await ClearSessionAsync();
        _user = null;
        _plugins = [];
        _selected = null;
        _credentials = [];
    }

    private async Task ClearSessionAsync()
    {
        _token = null;
        await JS.InvokeVoidAsync("quantumWorkspace.clearSessionToken");
    }

    private async Task SwitchViewAsync(string view)
    {
        _view = view;
        if (view == "credentials")
        {
            await RunAsync(LoadCredentialsAsync);
        }
        else if (view == "review")
        {
            await RunAsync(LoadReviewsAsync);
        }
    }

    private string NavClass(string view) => _view == view ? "active" : string.Empty;
    private Task ShowPluginsAsync() => SwitchViewAsync("plugins");
    private Task ShowCredentialsAsync() => SwitchViewAsync("credentials");
    private Task ShowReviewAsync() => SwitchViewAsync("review");

    private async Task LoadPluginsAsync()
    {
        _plugins = await RpcAsync<PluginSummary[]>("ListManagedPlugins", new { });
        if (_selected is not null)
        {
            _selected = _plugins.SingleOrDefault(plugin => plugin.PluginId == _selected.PluginId);
        }
    }

    private void BeginNewPlugin()
    {
        _selected = null;
        _editing = true;
        _pluginId = _pluginName = _pluginDescription = _pluginTags = string.Empty;
        _releases = [];
    }

    private async Task SelectPluginAsync(PluginSummary plugin)
    {
        _selected = plugin;
        _editing = true;
        _pluginId = plugin.PluginId;
        _pluginName = plugin.Name;
        _pluginDescription = plugin.Description;
        _pluginTags = string.Join(", ", plugin.Tags);
        _releases = await RpcAsync<PluginReleaseSummary[]>("ListPluginReleases", new { pluginId = plugin.PluginId });
    }

    private Task SavePluginAsync() => RunAsync(async () =>
    {
        var parameters = new
        {
            pluginId = _pluginId,
            name = _pluginName,
            description = _pluginDescription,
            tags = _pluginTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(tag => tag.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray()
        };
        var method = _selected is null ? "CreatePlugin" : "UpdatePlugin";
        var saved = await RpcAsync<PluginSummary>(method, parameters);
        await LoadPluginsAsync();
        await SelectPluginAsync(_plugins.Single(plugin => plugin.PluginId == saved.PluginId));
        Notify("插件资料已保存。");
    });

    private Task DeletePluginAsync() => RunAsync(async () =>
    {
        if (_selected is null || !await JS.InvokeAsync<bool>("confirm", $"永久删除 {_selected.PluginId} 及其全部版本？"))
        {
            return;
        }
        await RpcAsync("DeletePlugin", new { pluginId = _selected.PluginId });
        _selected = null;
        _editing = false;
        _releases = [];
        await LoadPluginsAsync();
        Notify("插件已删除。");
    });

    private void SelectArchive(InputFileChangeEventArgs eventArgs)
    {
        _archive = eventArgs.File;
        _archiveName = _archive.Name;
    }

    private Task UploadReleaseAsync() => RunAsync(async () =>
    {
        if (_selected is null || _archive is null)
        {
            throw new InvalidOperationException("请选择插件 ZIP。");
        }
        await using var stream = _archive.OpenReadStream(256L * 1024 * 1024);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        var archive = buffer.ToArray();
        var sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(archive));
        await RpcAsync("UploadPluginRelease", new
        {
            pluginId = _selected.PluginId,
            version = _releaseVersion,
            quantumVersionSupport = _versionSupport,
            releaseNotes = _releaseNotes,
            packageArchiveBase64 = Convert.ToBase64String(archive),
            expectedSha256 = sha256
        });
        _archive = null;
        _archiveName = null;
        _releaseVersion = _releaseNotes = string.Empty;
        _releases = await RpcAsync<PluginReleaseSummary[]>("ListPluginReleases", new { pluginId = _selected.PluginId });
        Notify("版本已提交自动审核。");
    });

    private async Task LoadCredentialsAsync()
        => _credentials = await RpcAsync<CredentialClientSummary[]>("ListCredentialClients", new { });

    private void ToggleAllowedPlugin(string pluginId, bool enabled)
    {
        if (enabled) _allowedPlugins.Add(pluginId);
        else _allowedPlugins.Remove(pluginId);
    }

    private Task CreateCredentialAsync() => RunAsync(async () =>
    {
        _generatedCredential = await RpcAsync<CreateCredentialClientResponse>("CreateCredentialClient", new
        {
            clientId = _clientId,
            displayName = _clientName,
            allowedPluginIds = _allowedPlugins.Order(StringComparer.Ordinal).ToArray()
        });
        _confirmedPrivateKey = false;
        await LoadCredentialsAsync();
        Notify("Credential Client 已生成，请立即保存私钥。");
    });

    private Task DeleteCredentialAsync(CredentialClientSummary client) => RunAsync(async () =>
    {
        if (!await JS.InvokeAsync<bool>("confirm", $"删除 CI 身份 {client.ClientId}？"))
        {
            return;
        }
        await RpcAsync("DeleteCredentialClient", new { clientId = client.ClientId });
        await LoadCredentialsAsync();
        Notify("CI 身份已删除。");
    });

    private async Task DownloadPrivateJwksAsync()
        => await JS.InvokeVoidAsync(
            "quantumWorkspace.downloadText",
            $"{_generatedCredential!.Client.ClientId}.private.jwks.json",
            _generatedCredential.GeneratedKeySet.PrivateJsonWebKeySet);

    private async Task CopyClientIdAsync()
        => await JS.InvokeVoidAsync("quantumWorkspace.copyText", _generatedCredential!.Client.ClientId);

    private void CloseCredentialModal()
    {
        if (!_confirmedPrivateKey) return;
        _generatedCredential = null;
        _clientId = _clientName = string.Empty;
        _allowedPlugins.Clear();
    }

    private async Task LoadReviewsAsync()
    {
        _reviews = await RpcAsync<PluginReleaseSummary[]>("ListAllPluginReleases", new { status = PluginReleaseState.Pending });
        foreach (var release in _reviews) _reviewNotes.TryAdd(release.ReleaseId, string.Empty);
    }

    private Task ReviewAsync(PluginReleaseSummary release, PluginReleaseState status) => RunAsync(async () =>
    {
        await RpcAsync("ReviewPluginRelease", new
        {
            releaseId = release.ReleaseId,
            status,
            notes = _reviewNotes.GetValueOrDefault(release.ReleaseId)
        });
        await LoadReviewsAsync();
        Notify(status == PluginReleaseState.Published ? "版本已发布。" : "版本已拒绝。");
    });

    private void SetReviewNotes(string releaseId, string? notes)
        => _reviewNotes[releaseId] = notes ?? string.Empty;

    private async Task<T> RpcAsync<T>(string method, object parameters, bool authenticated = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "rpc");
        if (authenticated)
        {
            if (string.IsNullOrWhiteSpace(_token)) throw new InvalidOperationException("请先登录。");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }
        request.Content = JsonContent.Create(new { jsonrpc = "2.0", id = Guid.NewGuid().ToString("N"), method, @params = parameters }, options: JsonOptions);
        using var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"平台请求失败（HTTP {(int)response.StatusCode}）。");
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.TryGetProperty("error", out var rpcError))
            throw new InvalidOperationException(rpcError.GetProperty("message").GetString() ?? "平台拒绝了请求。");
        var result = document.RootElement.GetProperty("result");
        if (result.TryGetProperty("isSuccess", out var success) && !success.GetBoolean())
            throw new InvalidOperationException(result.TryGetProperty("message", out var message) ? message.GetString() : "平台拒绝了请求。");
        var value = result.TryGetProperty("value", out var resultValue) ? resultValue : result;
        return value.Deserialize<T>(JsonOptions) ?? throw new InvalidOperationException("平台返回了无效响应。");
    }

    private async Task RpcAsync(string method, object parameters, bool authenticated = true)
        => await RpcAsync<JsonElement>(method, parameters, authenticated);

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true;
        try { await action(); }
        catch (Exception exception) { Notify(exception.Message, true); }
        finally { _busy = false; }
    }

    private void Notify(string message, bool error = false)
    {
        _notice = message;
        _noticeError = error;
    }

    private static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? $"{bytes / 1024d / 1024d:F1} MB"
        : $"{Math.Max(1, bytes / 1024d):F1} KB";
    private static string StatusClass(PluginReleaseState status) => status switch
    {
        PluginReleaseState.Pending => "pending",
        PluginReleaseState.Published => "published",
        _ => "rejected"
    };
    private static string StatusLabel(PluginReleaseState status) => status switch
    {
        PluginReleaseState.Pending => "待审核",
        PluginReleaseState.Published => "已发布",
        _ => "已拒绝"
    };
}
