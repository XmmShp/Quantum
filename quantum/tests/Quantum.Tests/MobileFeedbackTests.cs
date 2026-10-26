using System.Text.Json;
using Quantum.Mobile.Internal;
using Quantum.MobileApp;

namespace Quantum.Tests;

public sealed class MobileFeedbackTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SuccessDoesNotExposeBusinessDataOrWireMessages(bool chinese)
    {
        var result = JsonSerializer.SerializeToElement(new { isSuccess = true, value = new { privateData = "sensitive business details" } });
        var text = MobileFeedback.Outcome(new("request", "succeeded", "wire implementation message", result), chinese);
        Assert.DoesNotContain("sensitive", text);
        Assert.DoesNotContain("wire", text);
        Assert.DoesNotContain("isSuccess", text);
        Assert.Contains(chinese ? "操作已完成" : "Completed", text);
    }

    [Theory]
    [InlineData("calendar_invalid_request", "待办标题", "task title")]
    [InlineData("ledger_invalid_amount", "有效金额", "valid amount")]
    public void KnownValidationErrorsUseClientLocalization(string code, string zh, string en)
    {
        var result = JsonSerializer.SerializeToElement(new { errorCode = code, message = "Internal server text" });
        var outcome = new BridgeOutcome("request", "failed", "Rejected", result);
        Assert.Contains(zh, MobileFeedback.Outcome(outcome, true));
        Assert.Contains(en, MobileFeedback.Outcome(outcome, false));
    }

    [Theory]
    [InlineData("mobile_view_stale")]
    [InlineData("mobile_sharing_disabled")]
    public void UnavailablePagesTellTheUserToRefresh(string code)
    {
        Assert.Contains("刷新页面", MobileFeedback.Failure(code, true));
        Assert.Contains("refresh the page", MobileFeedback.Failure(code, false));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("future-status")]
    public void UncertainResultsPreserveTheWarningAgainstDuplicateActions(string status)
    {
        Assert.Contains("避免重复提交", MobileFeedback.Outcome(new("request", status, "opaque"), true));
        Assert.Contains("before submitting again", MobileFeedback.Outcome(new("request", status, "opaque"), false));
        Assert.Contains("避免重复提交", MobileFeedback.Snapshot(true, false, true));
        Assert.Contains("仍待确认", MobileFeedback.Snapshot(false, true, true));
        Assert.Equal("页面已同步。", MobileFeedback.Snapshot(false, false, true));
    }

    [Theory]
    [InlineData("{\"errorCode\":\"private-code\",\"message\":\"private server path\"}")]
    [InlineData("{\"errorCode\":123}")]
    [InlineData("[]")]
    [InlineData("null")]
    public void UnrecognizedResultsNeverEchoPayloads(string json)
    {
        var outcome = new BridgeOutcome("request", "failed", "private server path", JsonSerializer.Deserialize<JsonElement>(json));
        var text = MobileFeedback.Outcome(outcome, false);
        Assert.DoesNotContain("private", text);
        Assert.Contains("could not be completed", text);
    }
}
