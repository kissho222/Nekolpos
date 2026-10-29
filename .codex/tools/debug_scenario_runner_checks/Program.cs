using System.Text.Json;
using System.Threading.Tasks;

var results = new List<object>();
async Task Case(string name, Func<Task> test)
{
    try { await test(); results.Add(new { name, passed = true }); }
    catch (Exception e) { results.Add(new { name, passed = false, error = e.ToString() }); }
}
void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
async Task Expect<T>(Task task) where T : Exception
{
    try { await task; throw new Exception("Expected " + typeof(T).Name); }
    catch (T) { }
}

await Case("pre_cancel_starts_nothing", async () => {
    using var c = new CancellationTokenSource(); c.Cancel(); int starts = 0;
    await Expect<OperationCanceledException>(PureAwait.AwaitScenarioTaskAsync(_ => { starts++; return Task.FromResult(1); },
        () => true, () => true, () => throw new Exception(), () => 0, _ => Task.CompletedTask,
        c.Token, 1, new())); Check(starts == 0, "factory invoked");
});
await Case("already_complete_no_advance", async () => {
    int calls = 0; int result = await PureAwait.AwaitScenarioTaskAsync(_ => Task.FromResult(7), () => true,
        () => true, () => calls++, () => 0, _ => Task.CompletedTask, default, 1, new());
    Check(result == 7 && calls == 0, "extra advance");
});
await Case("gated_200ms_advance_until_complete", async () => {
    var task = new TaskCompletionSource<int>(); double time = 0; var times = new List<double>();
    int result = await PureAwait.AwaitScenarioTaskAsync(_ => task.Task, () => true, () => true,
        () => { times.Add(time); if (times.Count == 3) task.SetResult(9); }, () => time,
        _ => { time += .1; return Task.CompletedTask; }, default, 2, new());
    Check(result == 9 && times.Count == 3 && times.Zip(times.Skip(1)).All(p => p.Second - p.First >= .199999), "interval or completion");
});
await Case("gate_completion_no_extra_advance", async () => {
    var task = new TaskCompletionSource<int>(); int calls = 0;
    int result = await PureAwait.AwaitScenarioTaskAsync(_ => task.Task, () => true,
        () => { task.SetResult(4); return true; }, () => calls++, () => 0,
        _ => Task.CompletedTask, default, 1, new()); Check(result == 4 && calls == 0, "extra advance");
});
await Case("closed_gate_never_advances", async () => {
    var task = new TaskCompletionSource<int>(); int calls = 0, frames = 0;
    await PureAwait.AwaitScenarioTaskAsync(_ => task.Task, () => true, () => false, () => calls++, () => frames * .1,
        _ => { if (++frames == 4) task.SetResult(2); return Task.CompletedTask; }, default, 2, new());
    Check(calls == 0, "gate bypassed");
});
await Case("timeout_cancels_uncooperative_task_without_wait", async () => {
    var task = new TaskCompletionSource<int>(); double time = 0; bool cancelled = false;
    await Expect<TimeoutException>(PureAwait.AwaitScenarioTaskAsync(ct => { ct.Register(() => cancelled = true); return task.Task; },
        () => true, () => false, () => throw new Exception(), () => time,
        _ => { time += .1; return Task.CompletedTask; }, default, .3, new()));
    Check(cancelled && !task.Task.IsCompleted, "no bounded cancellation"); task.SetResult(3);
});
await Case("parent_cancel_propagates", async () => {
    using var c = new CancellationTokenSource(); var task = new TaskCompletionSource<int>(); bool cancelled = false;
    await Expect<OperationCanceledException>(PureAwait.AwaitScenarioTaskAsync(ct => { ct.Register(() => cancelled = true); return task.Task; },
        () => true, () => false, () => throw new Exception(), () => 0,
        _ => { c.Cancel(); return Task.CompletedTask; }, c.Token, 1, new())); Check(cancelled, "not linked"); task.SetResult(1);
});
await Case("lifetime_loss_cancels", async () => {
    var task = new TaskCompletionSource<int>(); bool valid = true, cancelled = false;
    await Expect<OperationCanceledException>(PureAwait.AwaitScenarioTaskAsync(ct => { ct.Register(() => cancelled = true); return task.Task; },
        () => valid, () => false, () => throw new Exception(), () => 0,
        _ => { valid = false; return Task.CompletedTask; }, default, 1, new())); Check(cancelled, "not cancelled"); task.SetResult(1);
});
await Case("operation_fault_is_observed_and_cancelled", async () => {
    bool cancelled = false; var diagnostics = new PureAwait.ScenarioAwaitDiagnostics();
    await Expect<InvalidDataException>(PureAwait.AwaitScenarioTaskAsync<int>(ct => { ct.Register(() => cancelled = true); return Task.FromException<int>(new InvalidDataException()); },
        () => true, () => false, () => throw new Exception(), () => 0, _ => Task.CompletedTask, default, 1, diagnostics));
    Check(cancelled && diagnostics.observedOperationFailureType == nameof(InvalidDataException), "fault missing");
});
await Case("late_fault_observed_without_success_rewrite", async () => {
    var task = new TaskCompletionSource<int>(); double time = 0; var diagnostics = new PureAwait.ScenarioAwaitDiagnostics();
    Task result = PureAwait.AwaitScenarioTaskAsync(_ => task.Task, () => true, () => false, () => throw new Exception(), () => time,
        _ => { time += 1; return Task.CompletedTask; }, default, 1, diagnostics);
    await Expect<TimeoutException>(result); task.SetException(new InvalidDataException());
    Check(result.IsFaulted && result.Exception.InnerException is TimeoutException && diagnostics.observedOperationFailureType == nameof(InvalidDataException), "late result changed");
});
await Case("advance_failure_cancels_before_return", async () => {
    var task = new TaskCompletionSource<int>(); bool cancelled = false;
    await Expect<InvalidOperationException>(PureAwait.AwaitScenarioTaskAsync(ct => { ct.Register(() => cancelled = true); return task.Task; },
        () => true, () => true, () => throw new InvalidOperationException(), () => 0,
        _ => Task.CompletedTask, default, 1, new())); Check(cancelled, "not cancelled"); task.SetResult(1);
});
await Case("cancel_callback_fault_does_not_mask_timeout", async () => {
    var task = new TaskCompletionSource<int>(); double time = 0; var diagnostics = new PureAwait.ScenarioAwaitDiagnostics();
    await Expect<TimeoutException>(PureAwait.AwaitScenarioTaskAsync(ct => { ct.Register(() => throw new InvalidOperationException()); return task.Task; },
        () => true, () => false, () => throw new Exception(), () => time,
        _ => { time += 1; return Task.CompletedTask; }, default, 1, diagnostics));
    Check(diagnostics.cancellationFailureType == nameof(AggregateException), "cancel callback missing"); task.SetResult(1);
});
await Case("advance_completion_with_cancel_remains_cancelled", async () => {
    using var c = new CancellationTokenSource(); var task = new TaskCompletionSource<int>(); bool cancelled = false;
    await Expect<OperationCanceledException>(PureAwait.AwaitScenarioTaskAsync(ct => { ct.Register(() => cancelled = true); return task.Task; },
        () => true, () => true, () => { task.SetResult(1); c.Cancel(); }, () => 0,
        _ => Task.CompletedTask, c.Token, 1, new())); Check(cancelled, "cancel lost to completion");
});
await Case("factory_failure_is_cancelled_and_preserved", async () => {
    bool cancelled = false;
    await Expect<InvalidDataException>(PureAwait.AwaitScenarioTaskAsync<int>(ct => { ct.Register(() => cancelled = true); throw new InvalidDataException(); },
        () => true, () => false, () => throw new Exception(), () => 0,
        _ => Task.CompletedTask, default, 1, new())); Check(cancelled, "factory failure not cancelled");
});
await Case("negative_control_old_reject_leaves_queue_new_reject_clears", () => {
    OldRejectProbe.memory["batch"]="next"; OldRejectProbe.Run();
    Check(OldRejectProbe.memory.ContainsKey("batch"), "old defect not reproduced");
    RejectProbe.memory["batch"]="next"; RejectProbe.failSave=false; RejectProbe.Run();
    Check(!RejectProbe.memory.ContainsKey("batch"), "new reject retained queue"); return Task.CompletedTask;
});
await Case("reject_report_failure_still_clears_queue", async () => {
    RejectProbe.memory["batch"]="next"; RejectProbe.failSave=true;
    await Expect<InvalidOperationException>(Task.Run(() => RejectProbe.Run()));
    Check(!RejectProbe.memory.ContainsKey("batch"), "report IO prevented clear"); RejectProbe.failSave=false;
});
await Case("first_and_continued_start_exceptions_clear_queue", () => {
    foreach (string stage in new[]{"initial","continued"}) {
        bool queue=true; try { PureAwait.StartScenarioBatchRequest(() => throw new InvalidDataException(stage), () => queue=false); }
        catch(InvalidDataException) { }
        Check(!queue, stage+" kept queue");
    } return Task.CompletedTask;
});
await Case("negative_control_preservation_alone_not_enough", () => {
    bool oldContinues = LegacyBatch.ShouldContinue(true, true);
    Check(oldContinues, "old condition not reproduced");
    foreach (string status in new[]{"TestFailed","Cancelled","EnvironmentUnavailable","Rejected","Running","passed","",null})
        Check(!PureAwait.ShouldContinueScenarioBatch("Finished",status,true,true), "unsafe continuation: "+status);
    Check(PureAwait.ShouldContinueScenarioBatch("Finished","Passed",true,true), "valid completion rejected");
    Check(!PureAwait.ShouldContinueScenarioBatch("AwaitingShutdown","Passed",true,true), "early phase continued");
    Check(!PureAwait.ShouldContinueScenarioBatch("Finished","Passed",false,true), "save failure continued");
    Check(!PureAwait.ShouldContinueScenarioBatch("Finished","Passed",true,false), "scene failure continued");
    return Task.CompletedTask;
});
await Case("audit_report_failure_finally_clears_queue", () => {
    bool queue=true; string status="Passed"; string phase="AwaitingShutdown";
    try { try { phase="Finished"; throw new InvalidDataException("first save"); }
        catch { status="EnvironmentUnavailable"; throw new InvalidDataException("second save"); }
        finally { if(!PureAwait.ShouldContinueScenarioBatch(phase,status,true,true)) queue=false; }
    } catch(InvalidDataException) { }
    Check(!queue,"double report failure kept queue"); return Task.CompletedTask;
});
await Case("scheduled_batch_cleared_queue_does_not_start", () => {
    string current=""; int starts=0;
    PureAwait.RunScheduledScenarioBatchRequest("next",()=>current,value=>current=value,_=>starts++,()=>current="");
    Check(starts==0,"cleared queue still started"); return Task.CompletedTask;
});
await Case("scheduled_batch_replaced_queue_does_not_start", () => {
    string current="replacement"; int starts=0;
    PureAwait.RunScheduledScenarioBatchRequest("next",()=>current,value=>current=value,_=>starts++,()=>current="");
    Check(starts==0 && current=="replacement","stale callback changed new queue"); return Task.CompletedTask;
});
await Case("scheduled_batch_pops_and_starts_once", () => {
    string current="first\nlast"; int starts=0; string requested=null;
    Action callback=()=>PureAwait.RunScheduledScenarioBatchRequest("first\nlast",()=>current,value=>current=value,id=>{starts++;requested=id;},()=>current="");
    callback(); callback();
    Check(starts==1 && requested=="first" && current=="last","duplicate or wrong pop"); return Task.CompletedTask;
});
string json = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json); File.WriteAllText("memory-only-results.json", json);
return results.Any(item => JsonSerializer.Serialize(item).Contains("\"passed\":false")) ? 1 : 0;
