using System.Text.Json;
using Quantum.Mobile.Internal;

namespace Quantum.MobileApp;

public static class MobileFeedback
{
    public static string Outcome(BridgeOutcome outcome, bool chinese) => outcome.Status switch
    {
        "accepted" => Pick(chinese, "已排队，等待电脑处理…", "Queued; waiting for desktop…"),
        "running" => Pick(chinese, "电脑正在执行，请稍候。", "The desktop is working. Please wait."),
        "succeeded" => Pick(chinese, "已提交", "Submitted."),
        "failed" => Failure(ErrorCode(outcome.Result), chinese),
        _ => Uncertain(chinese)
    };

    public static string Snapshot(bool uncertain, bool pending, bool chinese)
        => uncertain ? Uncertain(chinese) : pending
            ? Pick(chinese, "上一项操作仍待确认，请勿重复提交。", "The previous action is still pending. Do not submit it again.")
            : Pick(chinese, "页面已同步。", "Page synced.");

    public static string Failure(string? code, bool chinese) => code switch
    {
        "mobile_attachment_invalid" or "mobile_attachment_unavailable" or "codex_attachment_invalid" => Pick(chinese, "附件不可用，请检查文件大小或重新选择。", "Attachment unavailable. Check its size or select it again."),
        "calendar_invalid_request" => Pick(chinese, "请检查待办标题、日期和时间后重试。", "Check the task title, date and time, then try again."),
        "ledger_invalid_amount" => Pick(chinese, "请输入有效金额，例如 12.50。", "Enter a valid amount, for example 12.50."),
        "ledger_invalid_request" => Pick(chinese, "请检查金额、分类和日期后重试。", "Check the amount, category and date, then try again."),
        "mobile_page_unavailable" or "mobile_view_stale" or "mobile_sharing_disabled" or "rpc_method_unavailable" => Pick(chinese, "页面暂不可用，请检查电脑上的插件并刷新页面。", "The page is unavailable. Check the desktop plugin and refresh the page."),
        _ => Pick(chinese, "操作未完成，请检查输入或在电脑上查看详情。", "The action could not be completed. Check your input or review it on the desktop.")
    };

    private static string Uncertain(bool chinese) => Pick(chinese,
        "执行结果尚未确认，请先在电脑上核对，避免重复提交。",
        "The outcome is not confirmed. Check the desktop before submitting again.");

    private static string? ErrorCode(JsonElement? result)
        => result is { ValueKind: JsonValueKind.Object } value &&
           value.TryGetProperty("errorCode", out var code) && code.ValueKind == JsonValueKind.String
            ? code.GetString() : null;

    private static string Pick(bool chinese, string zh, string en) => chinese ? zh : en;
}
