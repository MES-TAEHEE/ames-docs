using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace AMES.Web.Components;

/// <summary>
/// 모든 화면·컴포넌트의 공통 기반(Components/_Imports.razor 의 @inherits) — 처리 중에 다시 누른 클릭이 처리 뒤에 또 실행되지 않게 한다.
/// Blazor Server 는 한 회로의 이벤트를 차례로 처리하므로, 저장처럼 오래 걸리는 동기 처리 중에 누른 클릭은 대기열에 쌓였다가
/// 처리가 끝나자마자 그대로 실행된다(PP-LSB 적용을 두 번 누르면 저장이 두 번 돌던 원인). 마우스 이벤트(버튼·행 클릭)에만 두 규칙을 건다:
///  · 같은 핸들러가 아직 실행 중(비동기 대기 중)이면 새 클릭은 버린다.
///  · 처리(동기 구간 또는 비동기 완료까지)가 SlowThreshold 이상 걸렸으면, 끝난 직후 QueueWindow 안에 들어온 클릭은 처리 중에 쌓인
///    것으로 보고 버린다. 빠른 핸들러의 연속 클릭·입력·키보드 이벤트에는 영향이 없다.
/// 그 밖의 동작(호출 직후·완료 후 다시 그리기, 취소된 작업은 다시 그리지 않음)은 ComponentBase 와 같다.
/// 클릭 처리가 끝나면(버린 클릭 포함) 브라우저의 처리 중 표시(js/busy-buttons.js)를 끄도록 알린다.
/// </summary>
public abstract class AmesComponentBase : ComponentBase, IHandleEvent
{
    static readonly TimeSpan SlowThreshold = TimeSpan.FromMilliseconds(400);
    static readonly TimeSpan QueueWindow   = TimeSpan.FromMilliseconds(400);
    static readonly FieldInfo? DelegateField =
        typeof(EventCallbackWorkItem).GetField("_delegate", BindingFlags.Instance | BindingFlags.NonPublic);

    // 화면들이 @inject IJSRuntime JS 를 쓰므로 이름이 겹치지 않게 둔다
    [Inject] IJSRuntime AmesBusyJs { get; set; } = default!;

    readonly HashSet<MethodInfo> _runningClicks = new();
    DateTime _slowClickEndUtc = DateTime.MinValue;

    Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, object? arg)
    {
        if (arg is not MouseEventArgs) return Dispatch(callback, arg);

        var method = (DelegateField?.GetValue(callback) as Delegate)?.Method;
        if (DateTime.UtcNow - _slowClickEndUtc < QueueWindow) { SignalClickDone(); return Task.CompletedTask; }
        if (method is not null && _runningClicks.Contains(method)) { SignalClickDone(); return Task.CompletedTask; }
        return DispatchClick(callback, arg, method);
    }

    Task Dispatch(EventCallbackWorkItem callback, object? arg)
    {
        var task = callback.InvokeAsync(arg);
        var shouldAwait = task.Status != TaskStatus.RanToCompletion && task.Status != TaskStatus.Canceled;
        StateHasChanged();
        return shouldAwait ? RenderOnCompletion(task) : Task.CompletedTask;
    }

    Task DispatchClick(EventCallbackWorkItem callback, object? arg, MethodInfo? method)
    {
        var started = DateTime.UtcNow;
        Task task;
        try { task = callback.InvokeAsync(arg); }
        catch { SignalClickDone(); throw; }
        finally { MarkIfSlow(started); }

        var shouldAwait = task.Status != TaskStatus.RanToCompletion && task.Status != TaskStatus.Canceled;
        StateHasChanged();
        if (!shouldAwait) { SignalClickDone(); return Task.CompletedTask; }
        if (method is not null) _runningClicks.Add(method);
        return CompleteClick(task, method, started);
    }

    async Task CompleteClick(Task task, MethodInfo? method, DateTime started)
    {
        try { await RenderOnCompletion(task); }
        finally
        {
            if (method is not null) _runningClicks.Remove(method);
            MarkIfSlow(started);
            SignalClickDone();
        }
    }

    async Task RenderOnCompletion(Task task)
    {
        try { await task; }
        catch
        {
            if (task.IsCanceled) return;
            throw;
        }
        StateHasChanged();
    }

    // 기다리지 않는다 — 회로가 끊겼거나 프리렌더 중이면 조용히 넘어간다
    void SignalClickDone()
    {
        try { _ = AmesBusyJs.InvokeVoidAsync("amesBusy.done").AsTask().ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); }
        catch { }
    }

    void MarkIfSlow(DateTime started)
    {
        var now = DateTime.UtcNow;
        if (now - started >= SlowThreshold) _slowClickEndUtc = now;
    }
}
