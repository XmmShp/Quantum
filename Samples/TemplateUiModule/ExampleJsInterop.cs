using Microsoft.JSInterop;

namespace TemplateUiModule;

public class ExampleJsInterop(IJSRuntime jsRuntime) : IAsyncDisposable // 如果需要，可以在 C# 中进行 JS 互操作
{
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>(
        "import", "./_content/TemplateUiModule/exampleJsInterop.js").AsTask());

    public async ValueTask<string> Prompt(string message)
    {
        var module = await _moduleTask.Value;
        return await module.InvokeAsync<string>("showPrompt", message);
    }

    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            var module = await _moduleTask.Value;
            await module.DisposeAsync();
        }
    }
}
