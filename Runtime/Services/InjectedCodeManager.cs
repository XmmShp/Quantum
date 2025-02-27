using Microsoft.AspNetCore.Components;
using Quantum.Infrastructure.Models;

namespace Quantum.Runtime.Services;

/// <summary>
/// 管理注入到App.razor的代码片段
/// </summary>
internal class InjectedCodeManager : IInjectedCodeManager
{
    /// <summary>
    /// 添加到head部分的代码
    /// </summary>
    public List<MarkupString> HeadCodes { get; } = [];

    /// <summary>
    /// 添加到blazor.web.js之前的代码
    /// </summary>
    public List<MarkupString> PreBlazorCodes { get; } = [];


    /// <summary>
    /// 添加到blazor.web.js之后的代码
    /// </summary>
    public List<MarkupString> PostBlazorCodes { get; } = [];

    /// <summary>
    /// 添加代码到head部分
    /// </summary>
    /// <param name="code">HTML代码</param>
    public void AddToHead(string code) => HeadCodes.Add(new MarkupString(code));

    /// <summary>
    /// 添加代码到blazor.web.js之前
    /// </summary>
    /// <param name="code">HTML代码</param>
    public void AddPreBlazor(string code) => PreBlazorCodes.Add(new MarkupString(code));

    /// <summary>
    /// 添加代码到blazor.web.js之后
    /// </summary>
    /// <param name="code">HTML代码</param>
    public void AddPostBlazor(string code) => PostBlazorCodes.Add(new MarkupString(code));
}
