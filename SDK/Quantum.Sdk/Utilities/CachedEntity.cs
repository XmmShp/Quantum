namespace Quantum.Sdk.Utilities;

/// <summary>
/// 缓存实体类，用于管理需要异步更新的值
/// </summary>
/// <typeparam name="T">缓存值的类型</typeparam>
/// <param name="updateFunc">用于更新值的异步函数</param>
/// <param name="initValue">初始值</param>
public class CachedEntity<T>(Func<Task<Result<T>>> updateFunc, T initValue)
{
    /// <summary>
    /// 创建一个不需要更新的缓存实体
    /// </summary>
    /// <param name="initValue">初始值</param>
    public CachedEntity(T initValue) : this(() => Task.FromResult<Result<T>>(Result.Failure("No need to update.")), initValue)
    { }
    /// <summary>
    /// 获取或设置缓存的值
    /// </summary>
    public T Value { get; set; } = initValue;

    /// <summary>
    /// 获取实体是否已初始化
    /// </summary>
    public bool IsInitialized { get; private set; }
    /// <summary>
    /// 获取更新任务
    /// </summary>
    public Task UpdateTask { get; private set; } = Task.CompletedTask;
    /// <summary>
    /// 当值更新时触发的事件
    /// </summary>
    public event EventHandler<T>? OnUpdated;

    /// <summary>
    /// 检查并触发值的更新
    /// </summary>
    /// <returns>当前实体实例，用于链式调用</returns>
    public CachedEntity<T> Peek()
    {
        if (UpdateTask.IsCompleted)
        { UpdateTask = UpdateAsync(); }

        return this;
    }

    /// <summary>
    /// 异步更新值
    /// </summary>
    private async Task UpdateAsync()
    {
        var newValueResult = await updateFunc();
        IsInitialized = true;
        if (newValueResult.IsSuccess)
        {
            Value = newValueResult.Value!;
            OnUpdated?.Invoke(this, Value);
        }
    }
}
