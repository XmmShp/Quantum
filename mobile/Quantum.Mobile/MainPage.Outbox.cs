using System.Text.Json;
using Microsoft.Maui.Storage;
using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

public sealed partial class MainPage
{
    private readonly Label _actionFeedback = new() { FontSize = 12, TextColor = MobileTheme.Muted, IsVisible = false };
    private readonly SemaphoreSlim _outboxGate = new(1);
    private PendingAction? _outbox;
    private bool _savingAction;
    private PendingAction? _failedDraft;
    private bool _offerFailedDraft;
    private Button? _restoreDraft;
    private long _feedbackRevision;

    private sealed record PendingAction(BridgeSend Send, string MobileId, string PluginId, string PageId,
        DateTimeOffset ExpiresAt, Dictionary<string, JsonElement> Inputs);

    private void SetActionFeedback(string text)
    {
        _actionFeedback.Text = text;
        _actionFeedback.IsVisible = true;
        var revision = ++_feedbackRevision;
        _ = HideFeedbackAsync(revision);
    }

    private async Task HideFeedbackAsync(long revision)
    {
        await Task.Delay(4000);
        if (_feedbackRevision == revision && _pendingId is null && !_uncertain) _actionFeedback.IsVisible = false;
    }

    private void UpdateActionAvailability()
    {
        // Editing remains local and responsive while transport catches up.
        _pageContent.IsEnabled = true;
        _renderer?.SetActionsEnabled(!_savingAction && !_uncertain && _pendingId is null && (_preview || !_awaitingSnapshot &&
            _snapshot is not null && _snapshot.ExpiresAt > (_client?.UtcNow ?? DateTimeOffset.UtcNow)));
    }

    private async Task FlushOutboxAsync(CancellationToken token)
    {
        if (!await _outboxGate.WaitAsync(0, token)) return;
        var pending = _outbox;
        var client = _client;
        try
        {
            if (pending is null || client is null || _uncertain) return;
            if (pending.MobileId != client.Registration?.Device.Id)
            {
                _uncertain = true;
                ShowNotice(T("登录设备已变更，请先在电脑核对已保存的操作。", "Device registration changed. Check the saved action on desktop first."));
                return;
            }
            await UploadAttachmentsAsync(client, pending, token);
            // The platform resolves duplicate IDs before view expiry checks. A lost response
            // can therefore be retried after expiry without re-executing a completed action.
            SetActionFeedback(T("正在发送…", "Sending…"));
            await client.CallAsync<JsonElement>("send", pending.Send, token);
            if (_outbox == pending) SetActionFeedback(T("已排队，等待电脑处理…", "Queued; waiting for desktop…"));
        }
        catch (BridgeException e) when (e.Code is "mobile_rate_limited" or "mobile_desktop_offline")
        {
            if (_outbox == pending) SetActionFeedback(T("操作已保存，等待重连…", "Action saved; waiting to reconnect…"));
        }
        catch (BridgeException e)
        {
            if (_outbox == pending && pending is not null)
            {
                // A conflict may indicate that this ID already belongs to an executed command.
                if (e.Code is not ("mobile_attachment_invalid" or "mobile_attachment_unavailable" or "mobile_view_stale" or "mobile_sharing_disabled" or "mobile_action_expired" or "mobile_action_not_allowed" or "mobile_input_invalid" or "mobile_invalid_request" or "mobile_forbidden"))
                {
                    _uncertain = true;
                    SetActionFeedback(T("结果待核对", "Outcome needs checking"));
                    ShowNotice(MobileFeedback.Failure(e.Code, Chinese));
                }
                else Outcome(new(pending.Send.Id, "failed", e.Code, MobileJson.Element(new { errorCode = e.Code })));
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (_outbox == pending) SetActionFeedback(T("连接中断，操作已保存，正在重连…", "Connection lost. Action saved; reconnecting…"));
        }
        finally { _outboxGate.Release(); UpdateActionAvailability(); }
    }

    private async Task UploadAttachmentsAsync(BridgeClient client, PendingAction pending, CancellationToken token)
    {
        var invoke = MobileJson.Read<BridgeInvoke>(pending.Send.Body);
        foreach (var (inputId, value) in pending.Inputs)
        {
            if (value.ValueKind != JsonValueKind.Array) continue;
            foreach (var file in MobileAttachments.Read(value))
            {
                SetActionFeedback(T("正在上传 ", "Uploading ") + file.Name);
                var created = await client.CallAsync<JsonElement>("attachment/create", new
                {
                    desktopId = pending.Send.TargetId, viewId = invoke.ViewId, inputId, attachment = file
                }, token);
                if (created.TryGetProperty("ready", out var ready) && ready.GetBoolean()) continue;
                var path = OutgoingAttachmentStore.PathFor(file);
                if (!File.Exists(path)) throw new BridgeException("mobile_attachment_unavailable", "Selected file is no longer available.");
                var chunks = (int)((file.Size + MobileAttachments.ChunkBytes - 1) / MobileAttachments.ChunkBytes);
                await Parallel.ForEachAsync(Enumerable.Range(0, chunks), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (index, cancellation) =>
                {
                    var offset = index * MobileAttachments.ChunkBytes;
                    await using var stream = File.OpenRead(path);
                    stream.Position = offset;
                    var bytes = new byte[(int)Math.Min(MobileAttachments.ChunkBytes, file.Size - offset)];
                    await stream.ReadExactlyAsync(bytes, cancellation);
                    await client.CallAsync<JsonElement>("attachment/write", new { attachmentId = file.Id, offset, dataBase64 = Convert.ToBase64String(bytes) }, cancellation);
                });
                await client.CallAsync<JsonElement>("attachment/complete", new { attachmentId = file.Id }, token);
            }
        }
    }

    private void RetainFailedDraft()
    {
        if (_outbox is not { } pending) return;
        _failedDraft = pending;
        if (!_preview) _ = SaveFailedDraftAsync(pending);
        if (_snapshot?.PluginId == pending.PluginId && _snapshot.Page.Id == pending.PageId && _renderer?.IsDirty == false)
            _renderer.RestoreInputs(pending.Inputs);
        EnsureDraftRecovery();
    }

    private void EnsureDraftRecovery()
    {
        if (_restoreDraft is null)
        {
            _restoreDraft = Button(T("恢复上次提交的输入", "Restore submitted input"), () =>
            {
                if (_failedDraft is { } draft && _snapshot?.PluginId == draft.PluginId && _snapshot.Page.Id == draft.PageId)
                {
                    _renderer?.RestoreInputs(draft.Inputs);
                    SetActionFeedback(T("已恢复空白字段，现有输入已保留", "Restored empty fields; current input retained"));
                }
                return Task.CompletedTask;
            });
            _notice.Children.Add(_restoreDraft);
        }
        _restoreDraft.IsVisible = true;
    }

    private async Task SaveFailedDraftAsync(PendingAction draft)
    {
        try { await SecureStorage.Default.SetAsync(_storageKey + ".failed-draft", JsonSerializer.Serialize(draft, MobileJson.Options)); }
        catch (Exception) { /* The in-memory draft remains available for recovery. */ }
    }
}
