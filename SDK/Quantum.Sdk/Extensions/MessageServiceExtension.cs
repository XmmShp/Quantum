using AntDesign;

namespace Quantum.Infrastructure.Extensions;

/// <summary>
/// 消息服务扩展方法类
/// </summary>
public static class MessageServiceExtension
{
    /// <summary>
    /// 在异步操作执行期间显示加载消息
    /// </summary>
    /// <typeparam name="T">异步操作的返回类型</typeparam>
    /// <param name="messageService">消息服务</param>
    /// <param name="asyncAction">要执行的异步操作</param>
    /// <param name="loadingMessage">加载时显示的消息</param>
    /// <param name="onFinish">异步操作完成时执行的操作</param>
    /// <param name="onClose">加载消息关闭时执行的操作</param>
    /// <returns>异步操作的结果</returns>
    public static async Task<T> LoadingWhen<T>(
        this IMessageService messageService,
        Func<Task<T>> asyncAction,
        string loadingMessage,
        Action? onFinish = null,
        Action? onClose = null
        )
    {
        var task = messageService.Loading(loadingMessage, 0, onClose);
        try
        {
            return await asyncAction();
        }
        finally
        {
            onFinish?.Invoke();
            task.Start();
        }
    }


    /// <summary>
    /// 在异步操作执行期间显示加载消息（无返回值版本）
    /// </summary>
    /// <param name="messageService">消息服务</param>
    /// <param name="asyncAction">要执行的异步操作</param>
    /// <param name="loadingMessage">加载时显示的消息</param>
    /// <param name="onFinish">异步操作完成时执行的操作</param>
    /// <param name="onClose">加载消息关闭时执行的操作</param>
    public static async Task LoadingWhen(
        this IMessageService messageService,
        Func<Task> asyncAction,
        string loadingMessage,
        Action? onFinish = null,
        Action? onClose = null
        )
    {
        var task = messageService.Loading(loadingMessage, 0, onClose);
        try
        {
            await asyncAction();
        }
        finally
        {
            onFinish?.Invoke();
            task.Start();
        }
    }
}
