using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

public static class AsyncOperationExtensions
{
    public static TaskAwaiter<AsyncOperation> GetAwaiter(this AsyncOperation asyncOp)
    {
        var tcs = new TaskCompletionSource<AsyncOperation>();
        asyncOp.completed += operation => { tcs.SetResult(operation); };
        return tcs.Task.GetAwaiter();
    }
}