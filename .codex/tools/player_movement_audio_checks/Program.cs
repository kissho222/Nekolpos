using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using UnityEngine;

namespace UnityEngine
{
    public struct Vector2 { public float x,y; public Vector2(float a,float b){x=a;y=b;} }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 up => new(0,1,0);
        public static Vector3 zero => new();
        public float magnitude => MathF.Sqrt(x*x+y*y+z*z);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 ProjectOnPlane(Vector3 a,Vector3 normal) { if(normal.x!=0 || normal.y!=1 || normal.z!=0)throw new Exception("Unsupported projection"); return new(a.x,0,a.z); }
    }
    public sealed class Transform { public Vector3 position; }
    public sealed class CharacterController { public bool enabled=true,isGrounded=true; }
    public sealed class Collider { public string Name; public Collider(string n)=>Name=n; }
    public struct RaycastHit { public Collider collider; }
    public static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b);public static float Sqrt(float v)=>MathF.Sqrt(v); }
    public static class Time { public static float deltaTime=.1f,timeScale=1; }
    public enum KeyCode { Space }
    public static class Input { public static bool Down; public static bool GetKeyDown(KeyCode key)=>Down; }
}
namespace Nekolpos.System
{
    public static class DebugConsoleInput { public static bool IsBlocked; }
    public static class SettingsPanel { public static bool IsOpen; }
}
namespace Nekolpos.Audio
{
    public sealed record Observation(float Speed,bool Grounded,Collider Collider,float DeltaTime);
    public sealed class CharacterMovementAudioController
    {
        public int Suspends,Resets,Jumps;
        public readonly List<Observation> Observations=new();
        public void ObserveMovement(float speed,bool grounded,Collider collider,float deltaTime)=>Observations.Add(new(speed,grounded,collider,deltaTime));
        public void SuspendMovementObservation()=>Suspends++;
        public void ResetMovementState()=>Resets++;
        public void NotifyJumpStarted()=>Jumps++;
    }
}
public static class WalkDialogueController { public static bool HandledInputThisFrame; }
public class ProbeEnvironment
{
    public CharacterController characterController = new();
    public Nekolpos.Audio.CharacterMovementAudioController? movementAudio = new();
    public Transform transform = new();
    public Collider? currentWalkableCollider, currentGround;
    public object? catSupport;
    public bool isOnWalkableSurface=true,lastMovementBlockedByEdge,hasApprovedDescent,hasJumpReference;
    public float verticalVelocity,gravity=9.81f,jumpHeight=.02f,groundProbeDistance=.03f,unsupportedBodySeconds,jumpReferenceHeight;
    public bool ProbeWalkable=true, PostMoveWalkable=true;
    public Collider ProbeCollider=new("before-floor"),PostMoveCollider=new("after-floor");
    public Vector3 MoveDisplacement,LastSafe;
    public Vector3? RecoveryPosition;
    public Vector2 InputValue=new(1,1);
    public int LookCalls,MoveCalls,ProbeCalls,SafeUpdates,SafeWrites,RestoreImeCalls,ClearSupportCalls;
    public void Call(string name)=>GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(this,null);
    public void UpdateLookRotation()=>LookCalls++;
    public void SuspendMovementIme(){}
    public void RestoreMovementIme()=>RestoreImeCalls++;
    public void RecoverFromUnsafeFallIfNeeded(){if(RecoveryPosition.HasValue)transform.position=RecoveryPosition.Value;}
    public void RecoverFromUnsupportedBody(Vector2 input){}
    public Vector2 ReadMovementInput()=>InputValue;
    public void MovePlayer(Vector2 input){MoveCalls++;transform.position+=MoveDisplacement;ProbeWalkable=PostMoveWalkable;ProbeCollider=PostMoveCollider;}
    public void UpdateLastSafePosition()=>SafeUpdates++;
    public void SetLastSafePosition(Vector3 value){SafeWrites++;LastSafe=value;}
    public void ClearCatSupport(){catSupport=null;currentGround=null;ClearSupportCalls++;}
    public float GetStepDownReferenceHeight()=>123;
    public bool TryFindWalkableGround(Vector3 position,float distance,out RaycastHit hit){ProbeCalls++;hit=new(){collider=ProbeCollider};return ProbeWalkable;}
    public void RefreshWalkableState(){isOnWalkableSurface=TryFindWalkableGround(transform.position,groundProbeDistance,out _);}
    public void Teleport(Vector3 destination,bool update)=>GetType().GetMethod("TeleportTo")!.Invoke(this,new object[]{destination,update});
}
static class Program
{
    static List<object> results=new();static int failures;
    static void Require(bool value,string reason){if(!value)throw new Exception(reason);}
    static void Near(float a,float b)=>Require(Math.Abs(a-b)<.0001f,$"Expected {b}, got {a}");
    static void Check(string name,Action action)
    {
        Time.deltaTime=.1f;Time.timeScale=1;Input.Down=false;WalkDialogueController.HandledInputThisFrame=false;Nekolpos.System.DebugConsoleInput.IsBlocked=false;Nekolpos.System.SettingsPanel.IsOpen=false;
        try{action();results.Add(new{name,passed=true});}catch(Exception e){failures++;results.Add(new{name,passed=false,error=(e.InnerException??e).Message});}
    }
    static int Main()
    {
        foreach(bool current in new[]{ProbeContract.UsesExtendedRootState})
        {
            string prefix="selected-source/";
            ProbeEnvironment New()=>new SelectedProbe();
            Check(prefix+"real-horizontal-displacement",()=>{var p=New();p.MoveDisplacement=new(3,100,4);Time.deltaTime=.5f;p.Call("Update");var o=p.movementAudio!.Observations.Single();Near(o.Speed,10);Near(o.DeltaTime,.5f);Require(o.Collider==p.PostMoveCollider,"stale collider");Require(p.ProbeCalls==(current?3:2),"extra/missing ground query");});
            Check(prefix+"input-without-displacement-is-silent-speed",()=>{var p=New();p.InputValue=new(1,1);p.MoveDisplacement=new(0,-.1f,0);p.Call("Update");Near(p.movementAudio!.Observations.Single().Speed,0);});
            Check(prefix+"pre-movement-recovery-excluded-from-speed",()=>{var p=New();p.RecoveryPosition=new(100,20,100);p.MoveDisplacement=new(.1f,0,0);p.Call("Update");Near(p.movementAudio!.Observations.Single().Speed,1);});
            foreach(float dt in new[]{0f,-.1f})Check(prefix+"nonpositive-delta-"+dt,()=>{var p=New();Time.deltaTime=dt;p.MoveDisplacement=new(1,0,0);p.Call("Update");Near(p.movementAudio!.Observations.Single().Speed,0);});
            foreach(var gate in new[]{"valid","disabled","null-controller","airborne","nonwalkable","rising"})Check(prefix+"ground-gate-"+gate,()=>{var p=New();p.PostMoveWalkable=gate!="nonwalkable";p.characterController.enabled=gate!="disabled";p.characterController.isGrounded=gate!="airborne";if(gate=="null-controller")p.characterController=null!;p.verticalVelocity=gate=="rising"?1:0;p.Call("Update");Require(p.movementAudio!.Observations.Single().Grounded==(gate=="valid"),"wrong grounded gate");});
            foreach(var gate in new[]{"console","pause","settings"})Check(prefix+"blocked-suspends-no-observation-"+gate,()=>{var p=New();if(gate=="console")Nekolpos.System.DebugConsoleInput.IsBlocked=true;if(gate=="pause")Time.timeScale=0;if(gate=="settings")Nekolpos.System.SettingsPanel.IsOpen=true;p.Call("Update");Require(p.movementAudio!.Suspends==1&&p.movementAudio.Resets==0&&p.movementAudio.Observations.Count==0&&p.MoveCalls==0&&p.ProbeCalls==0,"blocked hook boundary");});
            Check(prefix+"audio-optional-movement-preserved",()=>{var p=New();p.movementAudio=null;p.MoveDisplacement=new(.1f,0,0);p.Call("Update");Require(p.MoveCalls==1&&p.SafeUpdates==1,"missing audio changed movement");});
            Check(prefix+"valid-jump-notification-once",()=>{var p=New();Input.Down=true;p.Call("TryStartJump");Require(p.movementAudio!.Jumps==1&&p.verticalVelocity>0,"jump missing/duplicate");Input.Down=false;p.Call("TryStartJump");Require(p.movementAudio.Jumps==1,"no-key jump repeated");});
            foreach(var gate in new[]{"no-key","dialogue-owned","disabled","airborne","nonwalkable","zero-gravity","zero-height"})Check(prefix+"jump-refused-"+gate,()=>{var p=New();Input.Down=gate!="no-key";WalkDialogueController.HandledInputThisFrame=gate=="dialogue-owned";p.characterController.enabled=gate!="disabled";p.characterController.isGrounded=gate!="airborne";p.isOnWalkableSurface=gate!="nonwalkable";if(gate=="zero-gravity")p.gravity=0;if(gate=="zero-height")p.jumpHeight=0;p.Call("TryStartJump");Require(p.movementAudio!.Jumps==0,"refused/nonjump notified");});
            foreach(bool enabled in new[]{false,true})Check(prefix+"teleport-resets-preserves-enabled-"+enabled,()=>{var p=New();p.characterController.enabled=enabled;p.verticalVelocity=5;p.lastMovementBlockedByEdge=true;p.Teleport(new(1,2,3),true);Require(p.movementAudio!.Resets==1&&p.movementAudio.Suspends==0&&p.characterController.enabled==enabled&&p.SafeWrites==1,"teleport reset or state");Near(p.transform.position.y,2);Near(p.verticalVelocity,0);Require(!p.lastMovementBlockedByEdge,"blocked state remained");});
            Check(prefix+"teleport-without-safe-update",()=>{var p=New();p.Teleport(new(1,2,3),false);Require(p.SafeWrites==0&&p.movementAudio!.Resets==1,"safe update ignored option");});
            Check(prefix+"disable-resets-without-suspend",()=>{var p=New();p.Call("OnDisable");Require(p.movementAudio!.Resets==1&&p.movementAudio.Suspends==0&&p.movementAudio.Observations.Count==0,"disable hook boundary");});
        }
        Console.WriteLine(JsonSerializer.Serialize(new{count=results.Count,failures,scope="Exact methods extracted from the requested PlayerSafety source; physics/input/transform and audio callback spies. No real Unity/physics/audio/Settings.",results},new JsonSerializerOptions{WriteIndented=true}));
        return failures==0?0:1;
    }
}
