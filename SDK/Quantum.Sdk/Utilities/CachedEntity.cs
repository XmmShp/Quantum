namespace Quantum.Sdk.Utilities;

public class CachedEntity<T>(Func<Task<Result<T>>> updateFunc, T initValue)
{
    public CachedEntity(T initValue) : this(() => Task.FromResult<Result<T>>(Result.Failure("No need to update.")), initValue)
    { }
    public T Value { get; set; } = initValue;

    public bool IsInitialized { get; private set; }
    public Task UpdateTask { get; private set; } = Task.CompletedTask;
    public event EventHandler<T>? OnUpdated;

    public CachedEntity<T> Peek()
    {
        if (UpdateTask.IsCompleted)
        { UpdateTask = UpdateAsync(); }

        return this;
    }

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
