using System.Text.Json;
var results = new List<object>();
void Check(bool valid,string reason) { if(!valid) throw new Exception(reason); }
void Reset() {
    DebugConsoleInput.owned=false; DebugConsoleInput.activeLease=null; DebugConsoleInput.releasedFrame=-1;
    OldDebugConsoleInput.owned=false; OldDebugConsoleInput.activeLease=null; OldDebugConsoleInput.releasedFrame=-1;
    DebugSaveSession.CanRunScenarios=true; DebugScenarioExecutor.IsRunning=true; Time.frameCount=10;
}
void Test(string name, Action check) {
    Reset(); try { check(); results.Add(new{name,passed=true}); }
    catch(Exception e) { results.Add(new{name,passed=false,error=e.Message}); }
}
Test("negative_control_old_api_rejects_running_scenario",()=>{
    var old=new OldChatUIController(); int sent=0; old.OnWaitInputCompleted+=()=>sent++;
    Check(!old.AdvanceIsolatedDebugMessage(),"old failure not reproduced"); old.Normal();
    Check(sent==0 && OldDebugConsoleInput.IsBlocked,"old block missing");
});
Test("normal_input_stays_blocked_scenario_api_advances",()=>{
    var ui=new ChatUIController(); int sent=0; ui.OnWaitInputCompleted+=()=>sent++;
    ui.Normal(); Check(sent==0 && DebugConsoleInput.IsBlocked,"normal input unblocked");
    Check(!ui.AdvanceIsolatedDebugMessage(),"general debug bypassed");
    Check(ui.AdvanceIsolatedScenarioMessage() && sent==1 && DebugConsoleInput.IsBlocked,"scenario path failed or global block changed");
});
Test("nonisolated_scenario_rejected",()=>{ DebugSaveSession.CanRunScenarios=false; Check(!new ChatUIController().AdvanceIsolatedScenarioMessage(),"not isolated"); });
Test("nonrunning_scenario_rejected_normal_still_works",()=>{
    DebugScenarioExecutor.IsRunning=false; var ui=new ChatUIController(); int sent=0; ui.OnWaitInputCompleted+=()=>sent++;
    Check(!ui.AdvanceIsolatedScenarioMessage(),"not running"); ui.Normal(); Check(sent==1,"normal changed");
});
Test("console_owned_blocks_scenario",()=>{DebugConsoleInput.owned=true;Check(!new ChatUIController().AdvanceIsolatedScenarioMessage(),"ownership bypass");});
Test("console_lease_blocks_scenario",()=>{DebugConsoleInput.activeLease=new object();Check(!new ChatUIController().AdvanceIsolatedScenarioMessage(),"lease bypass");});
Test("closing_frame_blocks_scenario_next_frame_allows",()=>{
    DebugConsoleInput.releasedFrame=10; var ui=new ChatUIController();
    Check(!ui.AdvanceIsolatedScenarioMessage(),"closing frame bypass"); Time.frameCount=11;
    Check(ui.AdvanceIsolatedScenarioMessage(),"subsequent frame blocked");
});
Test("menu_blocks_shared_core",()=>{Check(!new ChatUIController{menuInputBlocked=true}.AdvanceIsolatedScenarioMessage(),"menu bypass");});
Test("delayed_process_blocks_shared_core",()=>{Check(!new ChatUIController{delayedDialogueProcessCoroutine=new object()}.AdvanceIsolatedScenarioMessage(),"delay bypass");});
Test("typing_only_skips_no_next_event",()=>{
    var ui=new ChatUIController{isTyping=true};int sent=0;ui.OnWaitInputCompleted+=()=>sent++;
    Check(ui.AdvanceIsolatedScenarioMessage() && ui.skipTyping && sent==0,"typing behavior changed");
});
Test("visible_choices_not_advanced",()=>{
    var ui=new ChatUIController{choicePanel=new Panel{activeSelf=true}};int sent=0;ui.OnWaitInputCompleted+=()=>sent++;
    Check(!ui.AdvanceIsolatedScenarioMessage() && sent==0,"choice bypass");
});
Test("missing_choice_panel_not_advanced",()=>{Check(!new ChatUIController{choicePanel=null}.AdvanceIsolatedScenarioMessage(),"missing panel bypass");});
Test("visible_choices_while_typing_not_touched",()=>{
    var ui=new ChatUIController{isTyping=true,choicePanel=new Panel{activeSelf=true}};
    Check(!ui.AdvanceIsolatedScenarioMessage() && !ui.skipTyping,"choice typing touched");
});
Test("shared_core_normal_parity",()=>{
    DebugScenarioExecutor.IsRunning=false;
    foreach(bool menu in new[]{false,true})foreach(bool delayed in new[]{false,true})foreach(bool typing in new[]{false,true})foreach(bool choice in new[]{false,true}){
        var old=new OldChatUIController{menuInputBlocked=menu,delayedDialogueProcessCoroutine=delayed?new object():null,isTyping=typing,choicePanel=new Panel{activeSelf=choice}};
        var ui=new ChatUIController{menuInputBlocked=menu,delayedDialogueProcessCoroutine=delayed?new object():null,isTyping=typing,choicePanel=new Panel{activeSelf=choice}};
        int before=0,after=0;old.OnWaitInputCompleted+=()=>before++;ui.OnWaitInputCompleted+=()=>after++;
        old.Normal();ui.Normal();Check(before==after && old.skipTyping==ui.skipTyping,"normal UI behavior changed");
    }
});
var json=JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true});
Console.WriteLine(json);File.WriteAllText("input-results.json",json);
return results.Any(item=>JsonSerializer.Serialize(item).Contains("\"passed\":false"))?1:0;
