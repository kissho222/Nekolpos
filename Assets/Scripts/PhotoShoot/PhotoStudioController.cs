using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Nekolpos.System;
using Nekolpos.TimeSystem;

namespace Nekolpos.PhotoShoot
{
    /// <summary>Only MainPhotoScene owns this workflow; it never invokes game time or save APIs.</summary>
    [DefaultExecutionOrder(30000)]
    public sealed class PhotoStudioController : MonoBehaviour
    {
        [Serializable] public sealed class CameraPreset
        {
            public string name;
            public Vector3 position;
            public Vector3 rotation;
            public float fov;
        }
        [Serializable] private sealed class PresetFile { public List<CameraPreset> presets = new List<CameraPreset>(); }
        public Camera photoCamera;
        public PhotoShootCameraController freeCamera;
        public Transform subject;
        public Animator animator;
        public NekomataLookRigController lookRig;
        public Transform head;
        public Transform[] neckBones = Array.Empty<Transform>();
        public Transform[] eyes = Array.Empty<Transform>();
        public RoomLightingManager lighting;
        public Volume postVolume;
        public PlayableDirector[] directors = Array.Empty<PlayableDirector>();
        public string[] animationStates = Array.Empty<string>();
        public Vector2 headYawPitch;
        public Vector2 eyeYawPitch;
        [Header("撮影用可動域（X=上下、Y=左右）")]
        public bool ignoreHeadXLimit;
        public bool ignoreHeadYLimit;
        [Header("被写体Root / Inspectorで部位を動かすと自動でポーズ停止")]
        public Vector3 subjectPosition;
        public Vector3 subjectRotation;
        [Header("撮影Post設定（共有Profileではなく、ここで調整）")]
        public bool dofEnabled;
        [Range(.1f,20f)] public float focusDistance = .6f;
        [Range(1,32)] public float aperture = 5.6f;
        [Range(1,300)] public float focalLength = 50f;
        public bool bloomEnabled;
        [Range(0,5)] public float bloomIntensity = .15f;
        public bool exposureOverride;
        [Range(-5,5)] public float exposure;
        [SerializeField] private string presetDirectory;
        private readonly List<Transform> poseBones = new List<Transform>();
        private readonly Dictionary<Transform, Quaternion> neutral = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Quaternion> held = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Canvas, bool> canvasStates = new Dictionary<Canvas, bool>();
        private readonly Dictionary<string, string> fields = new Dictionary<string, string>();
        private readonly List<VolumeProfile> runtimeProfiles = new List<VolumeProfile>();
        private PresetFile presetFile = new PresetFile();
        private CameraPreset initialCamera;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Vector2 scroll;
        private bool ready, frozenLook, uiHidden, paused;
        private float savedSpeed = 1f;
        private readonly Dictionary<PlayableDirector, bool> playingDirectors = new Dictionary<PlayableDirector, bool>();
        private string presetName = "Capsule_LowAngle_01";
        private int directorIndex;
        private bool guides = true;
        private Vector3 lastSubjectPosition;
        private Quaternion lastSubjectRotation;
        private readonly Dictionary<Transform, BonePose> observedBones = new Dictionary<Transform, BonePose>();
        private readonly Dictionary<Transform, BonePose> editedBones = new Dictionary<Transform, BonePose>();
        private struct BonePose
        {
            public Vector3 position, scale;
            public Quaternion rotation;
            public BonePose(Transform bone) { position=bone.localPosition;rotation=bone.localRotation;scale=bone.localScale; }
            public bool Matches(Transform bone) => position==bone.localPosition && scale==bone.localScale && Quaternion.Angle(rotation,bone.localRotation)<.01f;
            public void Apply(Transform bone) { bone.localPosition=position;bone.localRotation=rotation;bone.localScale=scale; }
        }
        private float guideAspect = 16f / 9f;
        private readonly List<Transform> editableBones = new List<Transform>();
        private int boneIndex;
        private LightTimeOfDay time = LightTimeOfDay.Morning;
        private WeatherType weather = WeatherType.Sunny;
        public bool UIHidden => uiHidden;
        public bool LookFrozen => frozenLook;
        public bool PosePaused => paused;
        public bool IsStudioActive => ready && enabled && gameObject.scene.name == "MainPhotoScene";
        public string PresetFilePath => Path.Combine(string.IsNullOrEmpty(presetDirectory)
            ? Path.Combine(Application.persistentDataPath, "PhotoStudio") : presetDirectory, "camera-presets.json");
        public IReadOnlyList<CameraPreset> Presets => presetFile.presets;
        public string LastCapturePath { get; private set; }

        private void Awake()
        {
            if (gameObject.scene.name != "MainPhotoScene") { enabled = false; return; }
            if (photoCamera == null || subject == null || animator == null || lookRig == null)
            { Debug.LogError("[PhotoStudio] Required camera/subject/Animator/LookRig references are missing.", this); enabled = false; return; }
            ready = true;
            initialCamera = SnapshotCamera("Reset");
            initialPosition = subject.position;
            initialRotation = subject.rotation;
            subjectPosition = lastSubjectPosition = subject.position;
            subjectRotation = subject.eulerAngles;
            lastSubjectRotation = subject.rotation;
            AddBone(head);
            foreach (var bone in neckBones) AddBone(bone);
            foreach (var bone in eyes) AddBone(bone);
            lookRig.enabled = false;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Update(0f);
            foreach (var bone in poseBones) neutral[bone] = bone.localRotation;
            // Instance profiles prevent edits to imported/shared production profiles.
            foreach (var volume in SceneComponents<Volume>())
                if (volume.sharedProfile != null)
                {
                    var copy = CloneProfile(volume.sharedProfile);
                    runtimeProfiles.Add(copy);
                    volume.profile = copy;
                }
            if (postVolume != null)
            {
                var profile = postVolume.profile;
                if (!profile.TryGet(out DepthOfField _)) profile.Add<DepthOfField>();
                if (!profile.TryGet(out Bloom _)) profile.Add<Bloom>();
                if (!profile.TryGet(out ColorAdjustments _)) profile.Add<ColorAdjustments>();
                profile.TryGet(out DepthOfField dof);
                dof.active = true;
                dof.mode.Override(dofEnabled ? DepthOfFieldMode.Bokeh : DepthOfFieldMode.Off);
                profile.TryGet(out Bloom bloom);
                bloom.active = true;
                bloom.intensity.Override(bloomEnabled ? bloom.intensity.value : 0f);
            }
            foreach (var director in directors)
            {
                director.playOnAwake = false;
                if (director.playableAsset == null) continue;
                // Animation tracks remain bound; game progression signals cannot fire in the studio.
                foreach (var output in director.playableAsset.outputs)
                    if (output.sourceObject is UnityEngine.Timeline.SignalTrack) director.ClearGenericBinding(output.sourceObject);
            }
            LoadPresets();
            EnableLook();
            foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var bone in renderer.bones)
                    if (bone != null && bone != subject && !observedBones.ContainsKey(bone))
                    { observedBones[bone] = new BonePose(bone); editableBones.Add(bone); }
            ApplyPostSettings();
        }

        private void AddBone(Transform bone) { if (bone != null && !poseBones.Contains(bone)) poseBones.Add(bone); }
        private T[] SceneComponents<T>() where T : Component
        {
            var result = new List<T>();
            foreach (var root in gameObject.scene.GetRootGameObjects()) result.AddRange(root.GetComponentsInChildren<T>(true));
            return result.ToArray();
        }
        public static VolumeProfile CloneProfile(VolumeProfile source)
        {
            var clone = ScriptableObject.CreateInstance<VolumeProfile>();
            clone.name = "PhotoStudio runtime profile";
            foreach (var component in source.components) clone.components.Add(Instantiate(component));
            return clone;
        }
        private void Update()
        {
            if (!ready) return;
            if (Input.GetKeyDown(KeyCode.F1)) ToggleUI();
            if (freeCamera != null) freeCamera.acceptKeyboardInput = uiHidden || GUIUtility.keyboardControl == 0 || Input.GetMouseButton(1);
            if (subject.position != lastSubjectPosition || Quaternion.Angle(subject.rotation,lastSubjectRotation)>.001f)
            { subjectPosition=subject.position;subjectRotation=subject.eulerAngles; }
            SynchronizeEditorBoneEdits();
            if (lookRig != null) lookRig.SetPhotoAngleLimitOverrides(ignoreHeadXLimit,ignoreHeadYLimit);
        }
        private void OnDestroy()
        {
            foreach (var profile in runtimeProfiles)
            {
                foreach (var component in profile.components) Destroy(component);
                Destroy(profile);
            }
        }
        private void LateUpdate()
        {
            if (!ready) return;
            subject.SetPositionAndRotation(subjectPosition,Quaternion.Euler(subjectRotation));
            lastSubjectPosition=subject.position;lastSubjectRotation=subject.rotation;
            foreach(var pair in editedBones) if(pair.Key!=null)pair.Value.Apply(pair.Key);
            if (frozenLook)
            {
            foreach (var bone in poseBones) if (bone != null && held.TryGetValue(bone, out var rotation)) bone.localRotation = rotation;
            if (head != null) head.localRotation *= Quaternion.Euler(headYawPitch.y, headYawPitch.x, 0f);
            foreach (var eye in eyes) if (eye != null) eye.localRotation *= Quaternion.Euler(eyeYawPitch.y, eyeYawPitch.x, 0f);
            }
            ObservePose();
            ApplyPostSettings();
        }
        private void ObservePose()
        {
            foreach(var bone in editableBones) if(bone!=null) observedBones[bone]=new BonePose(bone);
        }
        // Editor Undo must reach the pose cache before LateUpdate reapplies it.
        public void SynchronizeEditorBoneEdits()
        {
            if (!IsStudioActive) return;
            bool edited = false;
            foreach (var pair in observedBones)
                if (pair.Key != null && !pair.Value.Matches(pair.Key))
                { editedBones[pair.Key] = new BonePose(pair.Key); edited = true; }
            if (edited) { SetPaused(true); HoldLook(); }
        }
        public Transform[] GetEditableBones()
        {
            if (ready) return editableBones.ToArray();
            var bones = new HashSet<Transform>();
            if (animator != null)
                foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    foreach (var bone in renderer.bones)
                        if (bone != null && bone != subject) bones.Add(bone);
            var result = new List<Transform>(bones);
            result.Sort((a,b) => string.Compare(a.name,b.name,StringComparison.Ordinal));
            return result.ToArray();
        }
        public void SetBonePose(Transform bone, Vector3 localPosition, Quaternion localRotation)
        {
            if (bone==null || !bone.IsChildOf(subject)) throw new ArgumentException("Choose a bone under the photo subject.");
            bone.localPosition=localPosition;bone.localRotation=localRotation;
            editedBones[bone]=new BonePose(bone);
            SetPaused(true);HoldLook();ObservePose();
        }
        public void PlayAnimation(string state)
        {
            editedBones.Clear();animator.Play(state,0,0);animator.Update(0);ObservePose();
        }
        public void EnableLook()
        {
            frozenLook = false;
            foreach(var bone in poseBones)editedBones.Remove(bone);
            headYawPitch = eyeYawPitch = Vector2.zero;
            lookRig.enabled = true;
            lookRig.SetEventTarget(photoCamera.transform);
            lookRig.ResetExternalWeight(true);
        }
        public void HoldLook()
        {
            // Capture before OnDisable zeros Rig weights. Reapply after Animator/Rig each frame.
            held.Clear();
            foreach (var bone in poseBones) held[bone] = bone.localRotation;
            lookRig.enabled = false;
            headYawPitch = eyeYawPitch = Vector2.zero;
            frozenLook = true;
            LateUpdate();
        }
        public void FaceForward()
        {
            lookRig.enabled = false;
            held.Clear();
            foreach (var bone in poseBones) held[bone] = neutral[bone];
            headYawPitch = eyeYawPitch = Vector2.zero;
            frozenLook = true;
            LateUpdate();
        }
        public void ToggleUI()
        {
            if (!uiHidden)
            {
                canvasStates.Clear();
                foreach (var canvas in SceneComponents<Canvas>()) { canvasStates[canvas] = canvas.enabled; canvas.enabled = false; }
            }
            else foreach (var pair in canvasStates) if (pair.Key != null) pair.Key.enabled = pair.Value;
            uiHidden = !uiHidden;
        }
        public CameraPreset SnapshotCamera(string name) => new CameraPreset
            { name = name, position = photoCamera.transform.position, rotation = photoCamera.transform.eulerAngles, fov = photoCamera.fieldOfView };
        public void RestoreCamera(CameraPreset preset)
        {
            if (freeCamera != null) freeCamera.SetPose(preset.position, Quaternion.Euler(preset.rotation));
            else photoCamera.transform.SetPositionAndRotation(preset.position, Quaternion.Euler(preset.rotation));
            photoCamera.fieldOfView = Mathf.Clamp(preset.fov, 1f, 179f);
        }
        public void ResetCamera() => RestoreCamera(initialCamera);
        public void SavePreset(string name)
        {
            if (Nekolpos.System.DebugSaveSession.IsIsolated) return;
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Preset name is required.");
            var preset = SnapshotCamera(name.Trim());
            int index = presetFile.presets.FindIndex(p => p.name == preset.name);
            if (index < 0) presetFile.presets.Add(preset); else presetFile.presets[index] = preset;
            Directory.CreateDirectory(Path.GetDirectoryName(PresetFilePath));
            File.WriteAllText(PresetFilePath, JsonUtility.ToJson(presetFile, true));
        }
        public void LoadPresets()
        {
            if (!File.Exists(PresetFilePath)) return;
            try
            {
                var loaded = JsonUtility.FromJson<PresetFile>(File.ReadAllText(PresetFilePath));
                if (loaded == null || loaded.presets == null) throw new InvalidDataException("Invalid preset file.");
                foreach (var p in loaded.presets)
                    if (p == null || string.IsNullOrWhiteSpace(p.name) || !Finite(p.fov) || p.fov < 1f || p.fov > 179f ||
                        !Finite(p.position.x) || !Finite(p.position.y) || !Finite(p.position.z) ||
                        !Finite(p.rotation.x) || !Finite(p.rotation.y) || !Finite(p.rotation.z)) throw new InvalidDataException("Invalid camera values.");
                presetFile = loaded;
            }
            catch (Exception error) { Debug.LogWarning("[PhotoStudio] Camera presets were not loaded: " + error.Message, this); }
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public void SetLighting(LightTimeOfDay period, WeatherType condition)
        {
            time = period; weather = condition;
            if (lighting != null) lighting.SetLighting(period, condition);
        }
        public void SetPaused(bool value)
        {
            if (paused == value) return;
            if (value)
            {
                savedSpeed = animator.speed;
                animator.speed = 0f;
                playingDirectors.Clear();
                foreach (var d in directors) if (d != null) { playingDirectors[d] = d.state == PlayState.Playing; if (d.state == PlayState.Playing) d.Pause(); }
            }
            else
            {
                animator.speed = savedSpeed;
                foreach (var pair in playingDirectors) if (pair.Key != null && pair.Value) pair.Key.Resume();
            }
            paused = value;
        }
        public void StepPose()
        {
            SetPaused(true);
            animator.speed = savedSpeed;
            animator.Update(1f / 60f);
            animator.speed = 0f;
            foreach (var d in directors) if (d != null && d.playableGraph.IsValid()) { d.time = Math.Min(d.duration, d.time + 1d / 60d); d.Evaluate(); }
            LateUpdate();
        }
        public void ResetSubject() { subjectPosition=initialPosition;subjectRotation=initialRotation.eulerAngles;subject.SetPositionAndRotation(initialPosition, initialRotation);lastSubjectPosition=subject.position;lastSubjectRotation=subject.rotation;fields.Clear(); }
        public string Capture(int width = 1920, int height = 1080)
        {
            if (Nekolpos.System.DebugSaveSession.IsIsolated) return null;
            width = Mathf.Clamp(width, 64, 8192); height = Mathf.Clamp(height, 64, 8192);
            bool wasHidden = uiHidden;
            if (!wasHidden) ToggleUI();
            var rt = new RenderTexture(width, height, 24);
            Texture2D pixels = null;
            var oldActive = RenderTexture.active;
            float oldAspect = photoCamera.aspect;
            try
            {
                ApplyPostSettings();
                var cameraData = photoCamera.GetUniversalAdditionalCameraData();
                var manager = VolumeManager.instance;
                if (manager.isInitialized)
                    manager.Update(cameraData.volumeStack ?? manager.stack, cameraData.volumeTrigger != null ? cameraData.volumeTrigger : photoCamera.transform, cameraData.volumeLayerMask);
                photoCamera.aspect = (float)width / height;
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                if (!RenderPipeline.SupportsRenderRequest(photoCamera, request)) throw new InvalidOperationException("The active pipeline cannot capture this camera.");
                RenderPipeline.SubmitRenderRequest(photoCamera, request);
                RenderTexture.active = rt;
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                string safeName = string.Concat(presetName.Split(Path.GetInvalidFileNameChars()));
                var folder = Path.Combine(Application.persistentDataPath, "PhotoStudio", "Captures");
                Directory.CreateDirectory(folder);
                LastCapturePath = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + safeName + ".png");
                File.WriteAllBytes(LastCapturePath, pixels.EncodeToPNG());
                Debug.Log("[PhotoStudio] Saved: " + LastCapturePath, this);
                return LastCapturePath;
            }
            finally
            {
                photoCamera.aspect = oldAspect;
                RenderTexture.active = oldActive;
                rt.Release(); Destroy(rt); if (pixels != null) Destroy(pixels);
                if (!wasHidden) ToggleUI();
            }
        }
        private float Number(string key, float value)
        {
            GUI.SetNextControlName(key);
            if (!fields.ContainsKey(key) || GUI.GetNameOfFocusedControl() != key) fields[key] = value.ToString("0.####", CultureInfo.InvariantCulture);
            string previous = fields[key];
            fields[key] = GUILayout.TextField(previous, GUILayout.Width(85));
            if (fields[key] == previous) return value;
            return float.TryParse(fields[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && Finite(parsed) ? parsed : value;
        }
        private Vector3 Vector(string title, Vector3 value)
        {
            GUILayout.Label(title); GUILayout.BeginHorizontal();
            value = new Vector3(Number(title+"x",value.x),Number(title+"y",value.y),Number(title+"z",value.z));
            GUILayout.EndHorizontal(); return value;
        }
        private void OnGUI()
        {
            if (!ready || uiHidden) return;
            if (guides) DrawGuides();
            GUILayout.BeginArea(new Rect(12, 12, 380, Mathf.Max(200, Screen.height - 24)), GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Photo Studio / F1: UI表示切替");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("視線ON")) EnableLook();
            if (GUILayout.Button("角度保持OFF")) HoldLook();
            if (GUILayout.Button("正面へ")) FaceForward();
            GUILayout.EndHorizontal();
            ignoreHeadXLimit=GUILayout.Toggle(ignoreHeadXLimit,"首のX軸（上下）の可動域制限を無視");
            ignoreHeadYLimit=GUILayout.Toggle(ignoreHeadYLimit,"首のY軸（左右）の可動域制限を無視");
            GUILayout.Label(frozenLook ? "視線固定（Yaw / Pitch）" : "カメラ追従中");
            if (frozenLook)
            {
                headYawPitch = (Vector2)Vector("Head Yaw/Pitch", new Vector3(headYawPitch.x,headYawPitch.y,0));
                eyeYawPitch = (Vector2)Vector("Eye Yaw/Pitch", new Vector3(eyeYawPitch.x,eyeYawPitch.y,0));
            }
            if (GUILayout.Button("UIを隠す / F1で戻る")) ToggleUI();
            Vector3 cp = Vector("Camera Position", photoCamera.transform.position);
            Vector3 cr = Vector("Camera Rotation", photoCamera.transform.eulerAngles);
            if (cp != photoCamera.transform.position || cr != photoCamera.transform.eulerAngles)
                RestoreCamera(new CameraPreset{position=cp,rotation=cr,fov=photoCamera.fieldOfView});
            GUILayout.BeginHorizontal(); GUILayout.Label("FOV"); photoCamera.fieldOfView = Mathf.Clamp(Number("FOV",photoCamera.fieldOfView),1,179); GUILayout.EndHorizontal();
            GUILayout.Label("右ドラッグ:回転 / WASD・QE:移動 / Shift:高速");
            if (freeCamera != null) { GUILayout.BeginHorizontal(); GUILayout.Label("移動速度"); freeCamera.moveSpeed = Mathf.Max(.001f,Number("Speed",freeCamera.moveSpeed)); GUILayout.EndHorizontal(); }
            GUILayout.BeginHorizontal(); if (GUILayout.Button("Reset Camera")) ResetCamera();
            if (GUILayout.Button("Copy Values")) GUIUtility.systemCopyBuffer = JsonUtility.ToJson(SnapshotCamera(presetName),true); GUILayout.EndHorizontal();
            GUI.SetNextControlName("PresetName");
            presetName = GUILayout.TextField(presetName);
            if (GUILayout.Button("Save Camera Preset")) { try { SavePreset(presetName); } catch(Exception e) { Debug.LogError("[PhotoStudio] Preset save failed: "+e.Message,this); } }
            foreach(var p in presetFile.presets) if(GUILayout.Button(p.name)){RestoreCamera(p);presetName=p.name;}
            guides = GUILayout.Toggle(guides,"構図ガイド（三分割・中央・90% Safe Area）");
            GUILayout.BeginHorizontal(); GUILayout.Label("ガイド比率"); guideAspect = Mathf.Clamp(Number("Aspect",guideAspect),.2f,5); if(GUILayout.Button("16:9"))guideAspect=16f/9f; GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); if(GUILayout.Button(paused?"Resume Pose":"Pause Pose"))SetPaused(!paused); if(GUILayout.Button("1 Frame"))StepPose(); GUILayout.EndHorizontal();
            foreach(var state in animationStates) if(GUILayout.Button(state))PlayAnimation(state);
            if(directors.Length>0)
            {
                for(int i=0;i<directors.Length;i++)if(directors[i]!=null&&GUILayout.Button("Timeline: "+directors[i].name))directorIndex=i;
                var d=directors[directorIndex];
                if(d!=null&&d.gameObject.activeInHierarchy&&d.playableAsset!=null)
                {
                    GUILayout.BeginHorizontal(); if(GUILayout.Button("Play Timeline")){SetPaused(false);d.Play();} if(GUILayout.Button("Pause Timeline")){d.Pause();SetPaused(true);} GUILayout.EndHorizontal();
                    float t=GUILayout.HorizontalSlider((float)d.time,0,(float)d.duration);
                    if(Math.Abs(t-d.time)>.001){SetPaused(true);d.Pause();d.time=t;d.Evaluate();LateUpdate();}
                    GUILayout.Label("Timeline seconds: "+d.time.ToString("F3"));
                }
            }
            foreach(LightTimeOfDay period in Enum.GetValues(typeof(LightTimeOfDay)))if(GUILayout.Button(period.ToString()))SetLighting(period,weather);
            foreach(WeatherType condition in Enum.GetValues(typeof(WeatherType)))if(GUILayout.Button(condition.ToString()))SetLighting(time,condition);
            DrawPostControls();
            subjectPosition=Vector("Cat Position",subjectPosition);subjectRotation=Vector("Cat Rotation",subjectRotation);
            if(editableBones.Count>0)
            {
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("前の部位"))boneIndex=(boneIndex+editableBones.Count-1)%editableBones.Count;
                if(GUILayout.Button("次の部位"))boneIndex=(boneIndex+1)%editableBones.Count;
                GUILayout.EndHorizontal();
                var bone=editableBones[boneIndex];GUILayout.Label("部位: "+bone.name);
                var position=Vector("Bone Local Position",bone.localPosition);
                var rotation=Vector("Bone Local Rotation",bone.localEulerAngles);
                if(position!=bone.localPosition || rotation!=bone.localEulerAngles)SetBonePose(bone,position,Quaternion.Euler(rotation));
            }
            if(GUILayout.Button("Reset Cat"))ResetSubject();
            if(GUILayout.Button("Capture PNG 1920 x 1080（UIなし）")){try{Capture();}catch(Exception e){Debug.LogError("[PhotoStudio] Capture failed: "+e.Message,this);}}
            if(!string.IsNullOrEmpty(LastCapturePath))GUILayout.Label(LastCapturePath);
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        private void DrawPostControls()
        {
            if(postVolume==null)return;
            var profile=postVolume.profile;
            if(profile.TryGet(out DepthOfField dof))
            {
                dofEnabled=GUILayout.Toggle(dofEnabled,"DoF (Bokeh)");
                focusDistance=SliderNumber("Focus Distance (m)","Focus",focusDistance,.1f,20f);
                aperture=SliderNumber("Aperture (小さいほどぼける)","Aperture",aperture,1,32);
                focalLength=SliderNumber("Focal Length (mm)","FocalLength",focalLength,1,300);
                if(GUILayout.Button("頭にピントを合わせる"))FocusOnHead();
            }
            if(profile.TryGet(out Bloom bloom)){bloomEnabled=GUILayout.Toggle(bloomEnabled,"Bloom");bloomIntensity=SliderNumber("Bloom強度","Bloom",bloomIntensity,0,5);}
            if(profile.TryGet(out ColorAdjustments color)){exposureOverride=GUILayout.Toggle(exposureOverride,"Exposure上書き");exposure=SliderNumber("Exposure (EV)","Exposure",exposure,-5,5);}
        }
        private float SliderNumber(string label,string key,float value,float minimum,float maximum)
        {
            GUILayout.Label(label);
            GUILayout.BeginHorizontal();
            float slider=GUILayout.HorizontalSlider(Mathf.Clamp(value,minimum,maximum),minimum,maximum);
            if(!Mathf.Approximately(slider,Mathf.Clamp(value,minimum,maximum))) { value=slider;fields.Remove(key); }
            value=Number(key,value);
            GUILayout.EndHorizontal();
            return Mathf.Clamp(value,minimum,maximum);
        }
        public void FocusOnHead()
        {
            if(photoCamera==null || head==null)return;
            focusDistance=Mathf.Max(.1f,Vector3.Dot(head.position-photoCamera.transform.position,photoCamera.transform.forward));
            if(IsStudioActive)ApplyPostSettings();
        }
        public void ApplyPostSettings()
        {
            if(postVolume==null)return;
            // The studio owns the post settings; make the same profile visible to the photo camera.
            postVolume.enabled=true;postVolume.isGlobal=true;postVolume.weight=1;
            if(photoCamera!=null)
            {
                var data=photoCamera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing=true;
                data.requiresDepthOption=CameraOverrideOption.On;
                data.volumeLayerMask |= 1 << postVolume.gameObject.layer;
                photoCamera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.EveryFrame);
            }
            var profile=postVolume.profile;
            focalLength=Mathf.Clamp(focalLength,1,300);
            focusDistance=Mathf.Max(.1f,focalLength*.001f+.01f,focusDistance);
            if(profile.TryGet(out DepthOfField dof)) { dof.active=true;dof.mode.Override(dofEnabled?DepthOfFieldMode.Bokeh:DepthOfFieldMode.Off);dof.focusDistance.Override(focusDistance);dof.aperture.Override(Mathf.Clamp(aperture,1,32));dof.focalLength.Override(focalLength); }
            if(profile.TryGet(out Bloom bloom)){bloom.active=true;bloom.intensity.Override(bloomEnabled?Mathf.Max(0,bloomIntensity):0);}
            if(profile.TryGet(out ColorAdjustments color)){color.postExposure.overrideState=exposureOverride;color.postExposure.value=exposure;}
        }
        private void DrawGuides()
        {
            float w=Screen.width,h=w/guideAspect;if(h>Screen.height){h=Screen.height;w=h*guideAspect;}
            float x=(Screen.width-w)/2,y=(Screen.height-h)/2;
            var old=GUI.color;GUI.color=new Color(1,1,1,.55f);
            void Line(float lx,float ly,float lw,float lh)=>GUI.DrawTexture(new Rect(lx,ly,lw,lh),Texture2D.whiteTexture);
            for(int i=0;i<=3;i++){Line(x+w*i/3,y,1,h);Line(x,y+h*i/3,w,1);}
            Line(x+w/2-7,y+h/2,14,1);Line(x+w/2,y+h/2-7,1,14);
            Line(x+w*.05f,y+h*.05f,w*.9f,1);Line(x+w*.05f,y+h*.95f,w*.9f,1);
            Line(x+w*.05f,y+h*.05f,1,h*.9f);Line(x+w*.95f,y+h*.05f,1,h*.9f);GUI.color=old;
        }
    }
}
