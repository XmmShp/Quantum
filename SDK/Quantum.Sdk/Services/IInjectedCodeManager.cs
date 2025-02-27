namespace Quantum.Infrastructure.Models;

/// <summary>
/// 管理注入到App.razor的代码片段
/// </summary>
public interface IInjectedCodeManager
{
    /// <summary>
    /// 添加代码到head部分
    /// </summary>
    /// <param name="code">HTML代码</param>
    public void AddToHead(string code);

    /// <summary>
    /// 添加代码到blazor.web.js之前
    /// </summary>
    /// <param name="code">HTML代码</param>
    public void AddPreBlazor(string code);

    /// <summary>
    /// 添加代码到blazor.web.js之后
    /// </summary>
    /// <param name="code">HTML代码</param>
    public void AddPostBlazor(string code);
}
