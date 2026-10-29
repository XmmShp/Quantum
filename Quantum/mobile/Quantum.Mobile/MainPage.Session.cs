using System.Security.Cryptography;
using System.Text;
using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

public sealed partial class MainPage
{
    private bool _restoring;
    private string? _restoreAttemptedFor;
    private string? _authenticationPlatform;

    private PlatformSession CreateAuthentication(Uri platform)
    {
        _authenticationPlatform = platform.AbsoluteUri;
        var key = "quantum.mobile.session." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(platform.AbsoluteUri)));
        var session = new PlatformSession(_http, platform,
            () => SecureStorage.Default.GetAsync(key),
            value => SecureStorage.Default.SetAsync(key, value),
            () => { SecureStorage.Default.Remove(key); return Task.CompletedTask; });
        session.Changed += () =>
        {
            if (!ReferenceEquals(_authentication, session) || session.IsAuthenticated) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Suspend();
                _client = null;
                _snapshot = null;
                _renderer = null;
                _pageContent.Children.Clear();
                ShowSignIn();
                _signInError.Text = T("登录已过期或被撤销，请重新登录。", "Your session has expired or was revoked. Sign in again.");
                _signInError.IsVisible = true;
            });
        };
        return session;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if DEBUG
        if (_debugPreviewStartup) { _debugPreviewStartup = false; Preview(); return; }
#endif
        _ = RestoreLoginAsync();
    }

    private async Task RestoreLoginAsync(bool retry = false)
    {
        if (_restoring || _signingIn || _preview || _client is not null || !retry && _restoreAttemptedFor == _baseUrl) return;
        _restoring = true;
        _restoreAttemptedFor = _baseUrl;
        _signInButton.IsEnabled = false;
        _advancedButton.IsEnabled = false;
        _signInActivity.IsVisible = _signInActivity.IsRunning = true;
        try
        {
            var platform = PlatformAddress.Parse(_baseUrl);
            if (_authentication is null || _authenticationPlatform != platform.AbsoluteUri) _authentication = CreateAuthentication(platform);
            if (await _authentication.RestoreAsync()) await ConnectSessionAsync(platform);
        }
        catch (Exception exception)
        {
            _signInError.Text = T("暂时无法恢复登录，已保留登录信息：", "Could not restore sign-in yet; saved credentials were retained: ") + FriendlyError(exception);
            _signInError.IsVisible = true;
            _body.Children.Add(Button(T("重试恢复登录", "Retry saved sign-in"), () => RestoreLoginAsync(true)));
        }
        finally
        {
            _restoring = false;
            _signInButton.IsEnabled = true;
            _advancedButton.IsEnabled = true;
            _signInActivity.IsVisible = _signInActivity.IsRunning = false;
        }
    }

    private void ShowExpiredDevice()
    {
        ResetNavigation();
        _connected = false;
        _snapshot = null;
        _renderer = null;
        _desktopId = null;
        _client = null;
        _body.Children.Clear();
        _body.Children.Add(new Label { Text = T("设备连接已过期，请重新连接。", "The device connection expired. Reconnect to continue.") });
        _body.Children.Add(Button(T("重新连接设备", "Reconnect device"), async () =>
        {
            if (_storageKey.Length > 0) SecureStorage.Default.Remove(_storageKey);
            await ConnectSessionAsync(PlatformAddress.Parse(_baseUrl));
        }));
    }
}
