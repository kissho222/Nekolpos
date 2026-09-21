using System;
using System.Collections;
using System.Collections.Generic;
using Backgammon.Conversation;
using Nekolpos.ActionSystem;
using Nekolpos.Audio;
using Nekolpos.Data;
using Nekolpos.TimeSystem;
using Nekolpos.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Yarn.Unity;

namespace Nekolpos.System
{
    public sealed class OpenBetaTitleBootstrap : MonoBehaviour
    {
        public enum ConversationTimelinePhase
        {
            Start,
            End
        }

        [Serializable]
        public sealed class ConversationTimelineBinding
        {
            [Tooltip("会話CSVの action_id: play_conversation_timeline:<ID> から指定するIDです。")]
            public string id;
            [Tooltip("旧Scene設定との互換用の出発Directorです。基本会話地点の移動では再生時間を待たず、位置・姿勢・視線をコード側で確定します。")]
            public PlayableDirector startDirector;
            [Tooltip("旧Scene設定との互換用の到着Directorです。基本会話地点の移動では再生しません。")]
            public PlayableDirector endDirector;
            [Tooltip("オンなら、暗転完了イベント後に基本会話地点を更新します。Signal Receiverの個別設定は不要です。")]
            public bool changeHomeLocation;
            public CatHomeLocation destination = CatHomeLocation.Table;
            [Tooltip("旧設定との互換用です。位置更新は常に暗転完了後、明転前に実行されます。")]
            public ConversationTimelinePhase homeLocationChangePhase = ConversationTimelinePhase.Start;
            [Min(0f)]
            [Tooltip("旧設定との互換用です。暗転時間ではなくフェード完了イベントを同期点に使うため、実行時には参照しません。")]
            public float homeLocationChangeTime;
            [Tooltip("オンなら、この会話Timelineの完了後にタブレット閲覧カメラへ遷移します。")]
            public bool enterTabletCameraAfterPlayback;
        }

        private static OpenBetaTitleBootstrap activeTitleBootstrap;
        private static int nextTitleSessionId;
        private static int activeTitleSessionId;
        private static bool isDebugStart;

        private const string DefaultSteamUrl = "https://store.steampowered.com/";
        private const string SaveKeyPlayerName = "OpenBeta.PlayerName";
        private const string SaveKeyCatName = "OpenBeta.CatName";
        private const string SaveKeyPlayerCalling = "OpenBeta.PlayerCalling";
        private const string DefaultCatName = "ネルコ";
        private const string DefaultCatPronoun = "私";
        private const string CatRenameWordColor = "#FF9E63";
        private const string PlayerRenameWordColor = "#0066CC";
        private const string PendingRenameWordColor = "#FFD45A";

        [global::System.Diagnostics.Conditional("NEKOLPOS_VERBOSE_LOGS")]
        private static void VerboseLog(string message)
        {
            Debug.Log(message);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static void TitlePanelDebug(string message)
        {
            Debug.Log($"[TitlePanelDebug] frame={Time.frameCount} {message}");
        }
#endif

        [Header("Scene References")]
        [SerializeField] private OpenBetaIntroPopupPanel introPanel;
        [SerializeField] private OpenBetaInfoPanelController aboutPanel;
        [SerializeField] private OpenBetaInfoPanelController worldviewPanel;
        [SerializeField] private OpenBetaInfoPanelController trainingDataPanel;
        [SerializeField] private RectTransform creditPanel;
        [SerializeField] private OpenBetaCharacterSetupPanel characterSetupPanel;
        [SerializeField] private OpenBetaCallCatPanel callCatPanel;
        [SerializeField] private ChatUIController chatUI;
        [SerializeField] private ConversationGameStateManager gameStateManager;
        [SerializeField] private DialogueManager dialogueManager;
        [SerializeField] private DialogueEngine dialogueEngine;
        [SerializeField] private DialogueLogManager dialogueLogManager;
        [SerializeField] private YarnManager yarnManager;
        [SerializeField] private ConversationDataManager conversationDataManager;
        [SerializeField] private BackgroundMusicController backgroundMusicController;
        [SerializeField] private PlayableDirector timelineDirector;
        [SerializeField] private PlayableDirector initialCatCallTimelineDirector;
        [SerializeField] private PlayableDirector alternateMoveStartTimelineDirector;
        [SerializeField] private PlayableDirector alternateMoveEndTimelineDirector;
        [SerializeField] private Animator catAnimator;
        [SerializeField] private CatDataSO catData;
        [SerializeField] private YarnProject yarnProject;

        [Header("Startup Camera Pose")]
        [Tooltip("タイトル開始時に初期姿勢を適用するカメラ用 Pivot です。未設定時は CameraEventPivot を検索します。")]
        [SerializeField] private Transform startupCameraPivot;
        [Tooltip("タイトル開始時の CameraEventPivot のローカル座標です。")]
        [SerializeField] private Vector3 startupCameraLocalPosition = new Vector3(-0.222000018f, 0.42900002f, -1.15499997f);
        [Tooltip("タイトル開始時の CameraEventPivot のローカル回転（オイラー角）です。")]
        [SerializeField] private Vector3 startupCameraLocalEulerAngles = new Vector3(8.84322834f, 7.65155029f, -0.0000191169911f);

        [Header("Post Opening Camera Pose")]
        [Tooltip("OP完了後に適用する CameraEventPivot のローカル座標です。")]
        [SerializeField] private Vector3 postOpeningCameraLocalPosition = new Vector3(-0.223149061f, 0.419210255f, -1.6931212f);
        [Tooltip("OP完了後に適用する CameraEventPivot のローカル回転（オイラー角）です。")]
        [SerializeField] private Vector3 postOpeningCameraLocalEulerAngles = new Vector3(331.000031f, 7.21100712f, 1.09999788f);
        [Header("Desk Camera Pose")]
        [Tooltip("机側の基本会話拠点へ切り替えた時に適用する CameraEventPivot のローカル座標です。")]
        [SerializeField] private Vector3 deskHomeCameraLocalPosition = new Vector3(0.801f, 0.74f, -0.56f);
        [Tooltip("オンの間、編集モードで Desk Home Camera Local Position を変更すると CameraEventPivot へ即時反映します。")]
        [SerializeField] private bool previewDeskCameraPoseInEditor = true;

        [Header("Location Transition Timeline")]
        [Tooltip("ApplyLocationChange Signal が読み込むシーン名です。Build Settings に追加済みのシーン名かパスを指定してください。")]
        [SerializeField] private string locationTransitionSceneName;
        [SerializeField] private LoadSceneMode locationTransitionLoadMode = LoadSceneMode.Single;
        [SerializeField] [Min(0.05f)] private float locationTransitionFadeSeconds = 0.35f;
        [Tooltip("Timeline の FadeToBlack / FadeFromBlack Signal が使う既存のフェードです。未設定時はこの GameObject またはシーンから解決します。")]
        [SerializeField] private FadeController locationTransitionFadeController;
        [Tooltip("基本会話地点を移動する時の片道フェード時間です。暗転・明転を合わせて最低1秒の移動演出になります。")]
        [SerializeField] [Min(0.05f)] private float conversationLocationFadeSeconds = 0.5f;

        [Header("Conversation Timeline Actions")]
        [Tooltip("会話のActionから起動する通常Timelineです。場所移動は出発・到着Directorと切替時刻を1行へ登録します。")]
        [SerializeField] private ConversationTimelineBinding[] conversationTimelineBindings = Array.Empty<ConversationTimelineBinding>();

        [Header("Tablet Camera")]
        [Tooltip("タブレット閲覧時の実カメラ位置・回転を置くTransformです。")]
        [SerializeField] private Transform tabletCameraPoint;
        [SerializeField] [Min(0.01f)] private float tabletCameraTransitionSeconds = 0.4f;
        [Tooltip("PowerIconで画面を消した後、Desk会話カメラへ戻る時の反比例型イージングの強さです。値が大きいほど開始直後が速く、到着前はゆっくりになります。")]
        [SerializeField] [Range(0.1f, 8f)] private float tabletPowerReturnReciprocalStrength = 2f;
        [Tooltip("タブレット閲覧中に停止する通常視点操作コンポーネントです。現在のTitleSceneには該当コンポーネントがないため、追加時だけ登録してください。")]
        [SerializeField] private Behaviour[] tabletCameraInputControllers = Array.Empty<Behaviour>();
        [Tooltip("Deskで通常会話の入力待ち中だけ表示する、タブレット閲覧開始ボタンです。")]
        [SerializeField] private Button tabletButton;
        [Tooltip("タブレット本体の画面として表示するWorld Space Canvasです。通常時は消灯し、閲覧カメラ到着時だけ有効にします。")]
        [SerializeField] private GameObject tabletDisplayCanvas;
        private CanvasGroup tabletDisplayCanvasGroup;
        [Tooltip("タブレット画面の発光を担うEmissionPanelです。TabletCanvasと同じ電源状態で切り替えます。")]
        [SerializeField] private GameObject tabletEmissionPanel;
        [Tooltip("タブレット画面内の電源アイコンです。未設定時はPowerIconから実行時に解決します。")]
        [SerializeField] private Button tabletPowerButton;

        [Header("Animation Patterns")]
        [SerializeField] private OpenBetaTitleAnimationPatternSelection selectedAnimationPattern = OpenBetaTitleAnimationPatternSelection.Auto;
        [SerializeField] private OpenBetaTitleAnimationPattern patternA = new OpenBetaTitleAnimationPattern("A");
        [SerializeField] private OpenBetaTitleAnimationPattern patternB = new OpenBetaTitleAnimationPattern("B");

        [Header("Cat Animation States")]
        [SerializeField] private string preInitialCatCallStateName = "CatSimple_Lie_belly_sleep";
        [SerializeField] private string postInitialCatCallStateName = "CatSimple_Lie_belly_loop_1";
        [SerializeField] private float postInitialCatCallBlendSeconds = 0.25f;

        [Header("Cat Runtime Separation")]
        [SerializeField] private CatPresentationModeController catPresentationMode;
        [SerializeField] private GameObject opCatRoot;
        [SerializeField] private GameObject normalCatRoot;
        [Tooltip("ちゃぶ台側の基本会話位置。ゲーム開始時の既定拠点です。")]
        [SerializeField] private Transform normalCatHomeAnchor;
        [Tooltip("机側の基本会話位置。夜・タブレットイベントから切り替えます。")]
        [SerializeField] private Transform normalCatHomeDeskAnchor;
        [SerializeField] private bool createNormalCatCloneAtRuntime = true;

        [Header("External Links")]
        [SerializeField] private string steamUrl = DefaultSteamUrl;

        private bool waitingForOpeningTimeline;
        private bool waitingForInitialCatCallTimeline;
        private bool openingDialogueStarted;
        private string playerName;
        private string catName;
        private string playerCalling;
        private string catPronoun = DefaultCatPronoun;
        private bool wasCalledCorrectly;
        private bool waitingForInitialCatCall;
        private bool initialCatCallCompletionStarted;
        private bool runtimeFlowAborted;
        private int titleSessionId;
        // Legacy OP pose guards. Prefer OP_Cat/Normal_Cat separation through CatPresentationModeController for new work.
        private bool holdCatTransformAfterInitialCall;
        private bool keepPostInitialCatCallState;
        private Transform catHoldRoot;
        private Vector3 heldCatWorldPosition;
        private Quaternion heldCatWorldRotation;
        private TransformSnapshot[] heldCatPose;
        private Coroutine initialCatCallMonitorCoroutine;
        private Coroutine initialCatCallCompleteCoroutine;
        private Coroutine conversationTimelineCoroutine;
        private ConversationTimelineBinding activeConversationTimelineBinding;
        private FadeController activeConversationFadeController;
        private Action activeConversationOpaqueHandler;
        private Action activeConversationTransparentHandler;
        private bool isConversationFadeFlowActive;
        private Coroutine tabletCameraTransitionCoroutine;
        private Coroutine tabletPowerTransitionCoroutine;
        private Image tabletPowerFadeOverlay;
        private bool isTabletCameraViewActive;
        private bool hasSavedTabletCameraPose;
        private Vector3 savedTabletCameraPosition;
        private Quaternion savedTabletCameraRotation;
        private CursorLockMode savedTabletCursorLockMode;
        private bool savedTabletCursorVisible;
        private bool[] savedTabletInputControllerEnabledStates = Array.Empty<bool>();
        private bool chatUiSuppressedForTablet;
        private Coroutine alternateCatCallCoroutine;
        private Coroutine alternateMoveEndIdleCoroutine;
        private ChatUIController titleInputSubscriptionChatUI;
        private bool titleInputSubscriptionApplied;
        private OpenBetaTitleAnimationPattern activeAnimationPattern;
        private Transform alternateNeckBone;
        private Quaternion alternateNeckRestLocalRotation;
        private bool hasAlternateNeckRestLocalRotation;
        private Transform alternateArmCat;
        private TransformSnapshot alternateArmCatRestPose;
        private bool hasAlternateArmCatRestPose;
        private TransformSnapshot[] lastAlternateCompletedPose;
        private TransformSnapshot[] alternateMoveStartCompletedPose;
        private bool locationSceneLoadRequested;

        private void Reset()
        {
            EnsureBackgroundMusicControllerOnSelf();
        }

        private void OnValidate()
        {
            EnsureBackgroundMusicControllerOnSelf();

            bool shouldPreviewDeskCamera = !Application.isPlaying
                ? previewDeskCameraPoseInEditor
                : IsDeskHomeLocationActive();
            if (shouldPreviewDeskCamera)
            {
                ApplyHomeLocationCameraPose(CatHomeLocation.Desk);
            }
        }

        private void Awake()
        {
            if (!OpenBetaSceneNames.IsTitleLikeScene(SceneManager.GetActiveScene().name))
            {
                enabled = false;
                return;
            }

            NormalizeOpenBetaCanvasTransform();
            BeginTitleSession();

            try
            {
                ApplyStartupCameraPose();
                InitializeTitleSession();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private static void NormalizeOpenBetaCanvasTransform()
        {
            GameObject canvasObject = GameObject.Find("OpenBetaCanvas");
            if (canvasObject == null)
            {
                return;
            }

            Transform canvasTransform = canvasObject.transform;
            if (canvasTransform.localScale == Vector3.one)
            {
                return;
            }

            canvasTransform.localScale = Vector3.one;
            Debug.LogWarning("[OpenBetaTitle] OpenBetaCanvas localScale was normalized to (1, 1, 1).");
        }

        private void ApplyStartupCameraPose()
        {
            if (startupCameraPivot == null)
            {
                GameObject pivotObject = GameObject.Find("CameraEventPivot");
                startupCameraPivot = pivotObject != null ? pivotObject.transform : null;
            }

            if (startupCameraPivot == null)
            {
                Debug.LogWarning("[OpenBetaTitle] CameraEventPivot was not found; startup camera pose was not applied.", this);
                return;
            }

            startupCameraPivot.localPosition = startupCameraLocalPosition;
            startupCameraPivot.localRotation = Quaternion.Euler(startupCameraLocalEulerAngles);
        }

        private void ApplyPostOpeningCameraPose()
        {
            if (startupCameraPivot == null)
            {
                GameObject pivotObject = GameObject.Find("CameraEventPivot");
                startupCameraPivot = pivotObject != null ? pivotObject.transform : null;
            }

            if (startupCameraPivot == null)
            {
                Debug.LogWarning("[OpenBetaTitle] CameraEventPivot was not found; post-opening camera pose was not applied.", this);
                return;
            }

            startupCameraPivot.localPosition = postOpeningCameraLocalPosition;
            startupCameraPivot.localRotation = Quaternion.Euler(postOpeningCameraLocalEulerAngles);
        }

        public int TitleSessionId => titleSessionId;

        public static bool IsDebugStart => isDebugStart;

        public bool IsCurrentTitleSession
        {
            get
            {
                TryClaimCurrentTitleSessionIfNeeded("IsCurrentTitleSession");
                return activeTitleBootstrap == this &&
                       activeTitleSessionId == titleSessionId &&
                       OpenBetaSceneNames.IsTitleLikeScene(SceneManager.GetActiveScene().name);
            }
        }

        private void BeginTitleSession()
        {
            DisableStaleTitleBootstrapsInPersistentObjects();
            titleSessionId = ++nextTitleSessionId;
            activeTitleSessionId = titleSessionId;
            activeTitleBootstrap = this;
            runtimeFlowAborted = false;
            VerboseLog($"[OpenBetaTitle][Flow] Awake scene={SceneManager.GetActiveScene().name} frame={Time.frameCount} id={GetInstanceID()} session={titleSessionId}");
        }

        private void DisableStaleTitleBootstrapsInPersistentObjects()
        {
            OpenBetaTitleBootstrap[] bootstraps = Resources.FindObjectsOfTypeAll<OpenBetaTitleBootstrap>();
            int disabledCount = 0;
            for (int i = 0; i < bootstraps.Length; i++)
            {
                OpenBetaTitleBootstrap candidate = bootstraps[i];
                if (candidate == null || candidate == this || !candidate.enabled)
                {
                    continue;
                }

                if (candidate.gameObject != null &&
                    candidate.gameObject.scene.IsValid() &&
                    candidate.gameObject.scene != SceneManager.GetActiveScene())
                {
                    candidate.enabled = false;
                    disabledCount++;
                }
            }

            if (disabledCount > 0)
            {
                VerboseLog($"[OpenBetaTitle][Flow] Disabled stale title bootstraps count={disabledCount}");
            }
        }

        private void OnEnable()
        {
            TryClaimCurrentTitleSessionIfNeeded("OnEnable");
        }

        private bool TryClaimCurrentTitleSessionIfNeeded(string reason)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (this == null ||
                gameObject == null ||
                !isActiveAndEnabled ||
                !OpenBetaSceneNames.IsTitleLikeScene(activeScene.name))
            {
                return false;
            }

            if (activeTitleBootstrap == this && activeTitleSessionId == titleSessionId)
            {
                return true;
            }

            if (activeTitleBootstrap != null &&
                activeTitleBootstrap != this &&
                activeTitleBootstrap.gameObject != null &&
                activeTitleBootstrap.isActiveAndEnabled &&
                activeTitleSessionId > titleSessionId)
            {
                return false;
            }

            VerboseLog($"[OpenBetaTitle][Flow] ReclaimTitleSession reason={reason} oldActive={(activeTitleBootstrap != null ? activeTitleBootstrap.GetInstanceID().ToString() : "<null>")} oldSession={activeTitleSessionId} new={GetInstanceID()} session={titleSessionId}");
            activeTitleBootstrap = this;
            activeTitleSessionId = titleSessionId;
            return true;
        }

        private void InitializeTitleSession()
        {
            isDebugStart = false;
            VerboseLog($"[OpenBetaTitle][Flow] Awake set timeScale begin session={titleSessionId}");
            Time.timeScale = 1f;
            VerboseLog($"[OpenBetaTitle][Flow] Awake set timeScale end session={titleSessionId}");
            ResetGameManagerForTitleScreenSafely();
            VerboseLog($"[OpenBetaTitle][Flow] Awake resolving references session={titleSessionId}.");
            ResolveMissingReferences();
            BindTabletPowerButton();
            SetTabletDisplayPowered(false);
            BindTabletButton();
            VerboseLog($"[OpenBetaTitle][Flow] Awake resetting runtime state session={titleSessionId}.");
            ResetRuntimeStateForFreshTitleSession();
            LoadSetupValues();
            VerboseLog($"[OpenBetaTitle][Flow] Awake binding panels session={titleSessionId}.");
            BindPanels();
            HideDialogueUi();
            VerboseLog($"[OpenBetaTitle][Flow] Awake showing intro session={titleSessionId}.");
            ShowIntro();
        }

        private void ResetGameManagerForTitleScreenSafely()
        {
            GameManager manager = GameManager.Instance;
            if (manager == null)
            {
                VerboseLog("[OpenBetaTitle][Flow] GameManager reset skipped: instance is null.");
                return;
            }

            try
            {
                VerboseLog("[OpenBetaTitle][Flow] GameManager reset begin.");
                manager.ResetForTitleScreen();
                VerboseLog("[OpenBetaTitle][Flow] GameManager reset end.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void LateUpdate()
        {
            if (waitingForInitialCatCall)
            {
                EnsureTitleInputSubscription();
            }

            CompleteOpeningTimelineIfReachedEnd();
            RestoreHeldCatTransformNow();
            RefreshTabletButtonAvailability();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                RecoverTitleTimelinePlayback("focus");
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus)
            {
                RecoverTitleTimelinePlayback("pause");
            }
        }

        private void CompleteOpeningTimelineIfReachedEnd()
        {
            if (!waitingForOpeningTimeline || timelineDirector == null || timelineDirector.playableAsset == null)
            {
                return;
            }

            double duration = timelineDirector.duration;
            if (duration <= 0d || timelineDirector.time < duration - 0.001d)
            {
                if (timelineDirector.state != PlayState.Playing)
                {
                    ResumeDirectorFromCurrentTime(timelineDirector, "Opening");
                }

                return;
            }

            CompleteOpeningTimeline();
        }

        private void RestoreHeldCatTransformNow()
        {
            Transform holdRoot = GetCatHoldRoot();
            if (!holdCatTransformAfterInitialCall || holdRoot == null)
            {
                return;
            }

            EnsurePostInitialCatCallState();
            holdRoot.SetPositionAndRotation(heldCatWorldPosition, heldCatWorldRotation);
            ApplyHeldCatPose();
        }

        private void OnDestroy()
        {
            if (tabletButton != null)
            {
                tabletButton.onClick.RemoveListener(HandleTabletButtonClicked);
            }

            if (tabletPowerButton != null)
            {
                tabletPowerButton.onClick.RemoveListener(HandleTabletPowerButtonClicked);
            }

            if (tabletPowerTransitionCoroutine != null)
            {
                StopCoroutine(tabletPowerTransitionCoroutine);
                tabletPowerTransitionCoroutine = null;
            }

            StopConversationTimelinePlayback(resumePresentation: false);
            StopTabletCameraTransition(restoreSavedState: true);

            if (activeTitleBootstrap == this && activeTitleSessionId == titleSessionId)
            {
                activeTitleBootstrap = null;
                activeTitleSessionId = 0;
            }

            if (timelineDirector != null)
            {
                timelineDirector.stopped -= HandleTimelineStopped;
            }

            if (initialCatCallTimelineDirector != null)
            {
                initialCatCallTimelineDirector.stopped -= HandleInitialCatCallTimelineStopped;
            }

            if (alternateCatCallCoroutine != null)
            {
                StopCoroutine(alternateCatCallCoroutine);
                alternateCatCallCoroutine = null;
            }

            if (chatUI != null)
            {
                chatUI.RemoveTitleInputRoutingHandlers(HandlePlayerInputSubmitted, HandleDialogueBackRequested);
                chatUI.ClearTitleInputRouterIfMatches(this);
            }

            titleInputSubscriptionChatUI = null;
            titleInputSubscriptionApplied = false;

            if (initialCatCallCompleteCoroutine != null)
            {
                StopCoroutine(initialCatCallCompleteCoroutine);
                initialCatCallCompleteCoroutine = null;
            }

            if (initialCatCallMonitorCoroutine != null)
            {
                StopCoroutine(initialCatCallMonitorCoroutine);
                initialCatCallMonitorCoroutine = null;
            }
        }

        public void AbortRuntimeFlowForTitleReload()
        {
            isDebugStart = false;
            VerboseLog($"[OpenBetaTitle][Flow] AbortRuntimeFlowForTitleReload waitingInitial={waitingForInitialCatCall} waitingTimeline={waitingForInitialCatCallTimeline} openingStarted={openingDialogueStarted} chatUI={(chatUI != null)}");
            runtimeFlowAborted = true;
            enabled = false;
            waitingForOpeningTimeline = false;
            waitingForInitialCatCallTimeline = false;
            waitingForInitialCatCall = false;
            initialCatCallCompletionStarted = false;
            openingDialogueStarted = false;
            holdCatTransformAfterInitialCall = false;
            keepPostInitialCatCallState = false;

            StopTabletCameraTransition(restoreSavedState: true);
            StopAllCoroutines();
            alternateCatCallCoroutine = null;
            initialCatCallCompleteCoroutine = null;
            initialCatCallMonitorCoroutine = null;
            alternateMoveEndIdleCoroutine = null;

            if (timelineDirector != null)
            {
                timelineDirector.Stop();
                timelineDirector.time = 0d;
            }

            StopInitialCatCallDirectorWithoutRelease();
            StopAlternateCatCallSequence();

            if (chatUI != null)
            {
                chatUI.RemoveTitleInputRoutingHandlers(HandlePlayerInputSubmitted, HandleDialogueBackRequested);
                chatUI.ClearTitleInputRouterIfMatches(this);
                chatUI.ResetRuntimeStateForTitleRestart(true);
            }

            titleInputSubscriptionChatUI = null;
            titleInputSubscriptionApplied = false;

            if (activeTitleBootstrap == this && activeTitleSessionId == titleSessionId)
            {
                activeTitleBootstrap = null;
                activeTitleSessionId = 0;
            }
        }

        private void ResolveMissingReferences()
        {
            introPanel ??= FindFirstObjectByType<OpenBetaIntroPopupPanel>(FindObjectsInactive.Include);
            aboutPanel ??= FindAboutPanelByName("AboutPanel");
            worldviewPanel ??= FindAboutPanelByName("WorldviewPanel");
            trainingDataPanel ??= FindAboutPanelByName("TrainingDataPanel");
            creditPanel ??= FindSceneRectTransformByName("CreditPanel");
            characterSetupPanel ??= FindFirstObjectByType<OpenBetaCharacterSetupPanel>(FindObjectsInactive.Include);
            callCatPanel ??= FindFirstObjectByType<OpenBetaCallCatPanel>(FindObjectsInactive.Include);
            chatUI = ResolveActiveSceneReference(chatUI);
            if (tabletButton == null)
            {
                GameObject tabletButtonObject = GameObject.Find("TabletButton");
                tabletButton = tabletButtonObject != null ? tabletButtonObject.GetComponent<Button>() : null;
            }
            if (tabletDisplayCanvas == null)
            {
                RectTransform tabletCanvasTransform = FindSceneRectTransformByName("TabletCanvas");
                tabletDisplayCanvas = tabletCanvasTransform != null ? tabletCanvasTransform.gameObject : null;
            }
            if (tabletDisplayCanvasGroup == null && tabletDisplayCanvas != null)
            {
                tabletDisplayCanvasGroup = tabletDisplayCanvas.GetComponent<CanvasGroup>();
            }
            if (tabletPowerButton == null)
            {
                GameObject powerIcon = GameObject.Find("PowerIcon");
                if (powerIcon != null)
                {
                    tabletPowerButton = powerIcon.GetComponent<Button>();
                    if (tabletPowerButton == null)
                    {
                        tabletPowerButton = powerIcon.AddComponent<Button>();
                        tabletPowerButton.targetGraphic = powerIcon.GetComponent<Graphic>();
                    }
                }
            }
            gameStateManager ??= FindFirstObjectByType<ConversationGameStateManager>(FindObjectsInactive.Include);
            dialogueManager = ResolveDialogueManagerReference(dialogueManager);
            dialogueEngine ??= DialogueEngine.Instance ?? FindFirstObjectByType<DialogueEngine>(FindObjectsInactive.Include);
            dialogueLogManager ??= DialogueLogManager.Instance ?? FindFirstObjectByType<DialogueLogManager>(FindObjectsInactive.Include);
            yarnManager ??= FindFirstObjectByType<YarnManager>(FindObjectsInactive.Include);
            conversationDataManager ??= FindFirstObjectByType<ConversationDataManager>(FindObjectsInactive.Include);
            EnsureDiaryCalendarController();
            EnsureTabletHomeApplicationController();
            EnsureBackgroundMusicControllerOnSelf();

#if UNITY_WEBGL && !UNITY_EDITOR
            TitlePanelDebug(
                $"ResolveMissingReferences intro={FormatPanelDebug(introPanel)} " +
                $"about={FormatPanelDebug(aboutPanel)} " +
                $"worldview={FormatPanelDebug(worldviewPanel)} " +
                $"training={FormatPanelDebug(trainingDataPanel)} " +
                $"character={FormatPanelDebug(characterSetupPanel)} " +
                $"callCat={FormatPanelDebug(callCatPanel)}");
#endif

            timelineDirector ??= FindFirstObjectByType<PlayableDirector>(FindObjectsInactive.Include);
            ResolveAnimationPatterns();
            ApplyActiveAnimationPattern();
            PrepareSeparatedCatRuntime();
            catHoldRoot ??= ResolveCatHoldRoot(initialCatCallTimelineDirector, catAnimator);
            CacheAlternateNeckRestPose();

            if (trainingDataPanel == null)
            {
                Debug.LogError("[OpenBetaTitle] TrainingDataPanel reference could not be resolved.", this);
            }

            if (catData == null)
            {
                catData = ScriptableObject.CreateInstance<CatDataSO>();
            }

            if (dialogueManager != null)
            {
                dialogueManager.chatUI = chatUI;
                dialogueManager.catData = catData;
            }

            if (chatUI != null)
            {
                EnsureTitleInputSubscription();
            }
            else
            {
                Debug.LogWarning("[OpenBetaTitle][Flow] ChatUI is null during ResolveMissingReferences.");
            }

            EnsureYarnSystem();
            RebuildConversationSystem();
            backgroundMusicController.PlayTitleMusic(true);

            if (timelineDirector != null)
            {
                timelineDirector.Stop();
                timelineDirector.time = 0d;
                timelineDirector.stopped -= HandleTimelineStopped;
                timelineDirector.stopped += HandleTimelineStopped;
            }

            if (initialCatCallTimelineDirector != null)
            {
                initialCatCallTimelineDirector.extrapolationMode = DirectorWrapMode.Hold;
                initialCatCallTimelineDirector.Stop();
                initialCatCallTimelineDirector.time = 0d;
                initialCatCallTimelineDirector.stopped -= HandleInitialCatCallTimelineStopped;
                initialCatCallTimelineDirector.stopped += HandleInitialCatCallTimelineStopped;
            }

            PrepareAlternateCatCallDirector(alternateMoveStartTimelineDirector);
            PrepareAlternateCatCallDirector(alternateMoveEndTimelineDirector);
        }

        public void SetHomeLocation(CatHomeLocation location)
        {
            catPresentationMode?.SetHomeLocation(location);
            ApplyHomeLocationCameraPose(location);
        }

        public void MoveHomeToTable()
        {
            SetHomeLocation(CatHomeLocation.Table);
        }

        public void MoveHomeToDesk()
        {
            SetHomeLocation(CatHomeLocation.Desk);
        }

        public void MoveHomeToBathroom()
        {
            SetHomeLocation(CatHomeLocation.Bathroom);
        }

        public void MoveHomeToBed()
        {
            SetHomeLocation(CatHomeLocation.Bed);
        }

        /// <summary>
        /// 会話CSVの <c>play_conversation_timeline:&lt;ID&gt;</c>、またはTimeline Signalから、
        /// Inspector登録済みの通常Timelineを再生する。
        /// </summary>
        public void PlayConversationTimeline(string timelineId)
        {
            TryPlayConversationTimeline(timelineId);
        }

        public bool TryPlayConversationTimeline(string timelineId)
        {
            string normalizedId = timelineId?.Trim();
            if (string.IsNullOrEmpty(normalizedId))
            {
                Debug.LogWarning("[OpenBetaTitle] 会話TimelineのIDが空です。", this);
                return false;
            }

            ConversationTimelineBinding binding = null;
            foreach (ConversationTimelineBinding candidate in conversationTimelineBindings)
            {
                if (candidate != null && string.Equals(candidate.id?.Trim(), normalizedId, StringComparison.OrdinalIgnoreCase))
                {
                    binding = candidate;
                    break;
                }
            }

            if (binding == null)
            {
                Debug.LogWarning($"[OpenBetaTitle] 会話Timeline '{normalizedId}' は未登録です。", this);
                return false;
            }

            StopConversationTimelinePlayback(resumePresentation: false);
            catPresentationMode?.BeginTimelinePresentation();
            activeConversationTimelineBinding = binding;
            conversationTimelineCoroutine = StartCoroutine(PlayConversationTimelineRoutine(binding));
            Debug.Log($"[OpenBetaTitle] 会話拠点移動を開始: {normalizedId}", this);
            return true;
        }

        private IEnumerator PlayConversationTimelineRoutine(ConversationTimelineBinding binding)
        {
            FadeController fadeController = ResolveLocationTransitionFadeController();
            if (fadeController == null)
            {
                Debug.LogError($"[OpenBetaTitle] 会話Timeline '{binding.id}' は暗転用FadeControllerを解決できないため開始しません。", this);
                catPresentationMode?.ResumeIdleAfterTimeline();
                CompleteConversationTimeline(binding);
                yield break;
            }

            // The Timeline assets retain their Signal markers for non-conversation use.
            // This flow owns the fade so their callbacks cannot race the completion event.
            activeConversationFadeController = fadeController;
            isConversationFadeFlowActive = true;

            yield return FadeConversationToOpaque(fadeController);

            if (binding.changeHomeLocation)
            {
                ApplyConversationLocationWhileOpaque(binding.destination);
            }

            // A basic conversation location change must not keep the screen black for the
            // duration of its shared Timeline assets. The destination pose and gaze are
            // reconciled directly while opaque, then the normal idle controller takes over.
            catPresentationMode?.ResumeIdleAfterTimeline();
            catPresentationMode?.RefreshLookAtCameraImmediately();
            yield return new WaitForEndOfFrame();
            catPresentationMode?.RefreshLookAtCameraImmediately();

            yield return FadeConversationToTransparent(fadeController);

            // Present the completed Desk/Table state for one rendered frame before moving
            // into the optional tablet camera. This keeps the cat visible as the fade opens.
            yield return null;
            if (binding.enterTabletCameraAfterPlayback && !EnterTabletCameraView())
            {
                Debug.LogWarning($"[OpenBetaTitle] 会話Timeline '{binding.id}' 完了後のタブレットカメラ遷移を開始できません。", this);
            }

            CompleteConversationTimeline(binding);
        }

        private void ApplyConversationLocationWhileOpaque(CatHomeLocation destination)
        {
            SetHomeLocation(destination);
            catPresentationMode?.RefreshLookAtCameraImmediately();
        }

        private IEnumerator FadeConversationToOpaque(FadeController fadeController)
        {
            bool becameOpaque = fadeController.IsOpaque;
            if (!becameOpaque)
            {
                activeConversationOpaqueHandler = () => becameOpaque = true;
                fadeController.BecameOpaque += activeConversationOpaqueHandler;
                fadeController.FadeToBlack(conversationLocationFadeSeconds);
            }

            while (!becameOpaque && fadeController != null)
            {
                yield return null;
            }

            ClearConversationOpaqueHandler(fadeController);
        }

        private IEnumerator FadeConversationToTransparent(FadeController fadeController)
        {
            bool becameTransparent = fadeController.IsTransparent;
            if (!becameTransparent)
            {
                activeConversationTransparentHandler = () => becameTransparent = true;
                fadeController.BecameTransparent += activeConversationTransparentHandler;
                fadeController.FadeFromBlack(conversationLocationFadeSeconds);
            }

            while (!becameTransparent && fadeController != null)
            {
                yield return null;
            }

            ClearConversationTransparentHandler(fadeController);
        }

        private void ClearConversationFadeEventHandlers()
        {
            ClearConversationOpaqueHandler(activeConversationFadeController);
            ClearConversationTransparentHandler(activeConversationFadeController);
        }

        private void ClearConversationOpaqueHandler(FadeController fadeController)
        {
            if (fadeController != null && activeConversationOpaqueHandler != null)
            {
                fadeController.BecameOpaque -= activeConversationOpaqueHandler;
            }

            activeConversationOpaqueHandler = null;
        }

        private void ClearConversationTransparentHandler(FadeController fadeController)
        {
            if (fadeController != null && activeConversationTransparentHandler != null)
            {
                fadeController.BecameTransparent -= activeConversationTransparentHandler;
            }

            activeConversationTransparentHandler = null;
        }

        private void CompleteConversationTimeline(ConversationTimelineBinding binding)
        {
            isConversationFadeFlowActive = false;
            ClearConversationFadeEventHandlers();
            activeConversationFadeController = null;

            if (activeConversationTimelineBinding == binding)
            {
                conversationTimelineCoroutine = null;
                activeConversationTimelineBinding = null;
            }
        }

        private void BindTabletButton()
        {
            if (tabletButton == null)
            {
                return;
            }

            tabletButton.onClick.RemoveListener(HandleTabletButtonClicked);
            tabletButton.onClick.AddListener(HandleTabletButtonClicked);
            RefreshTabletButtonAvailability();
        }

        private void BindTabletPowerButton()
        {
            if (tabletPowerButton == null)
            {
                return;
            }

            tabletPowerButton.onClick.RemoveListener(HandleTabletPowerButtonClicked);
            tabletPowerButton.onClick.AddListener(HandleTabletPowerButtonClicked);
        }

        private void HandleTabletButtonClicked()
        {
            if (!CanOpenTabletFromButton())
            {
                RefreshTabletButtonAvailability();
                return;
            }

            EnterTabletCameraView();
        }

        private void HandleTabletPowerButtonClicked()
        {
            if (!isTabletCameraViewActive || tabletPowerTransitionCoroutine != null)
            {
                return;
            }

            tabletPowerTransitionCoroutine = StartCoroutine(PowerOffTabletAndReturnToDeskRoutine());
        }

        private IEnumerator PowerOffTabletAndReturnToDeskRoutine()
        {
            Image fadeOverlay = EnsureTabletPowerFadeOverlay();
            if (fadeOverlay != null)
            {
                Color overlayColor = fadeOverlay.color;
                overlayColor.a = 0f;
                fadeOverlay.color = overlayColor;
                fadeOverlay.gameObject.SetActive(true);

                const float fadeSeconds = 0.2f;
                float elapsed = 0f;
                while (elapsed < fadeSeconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    overlayColor.a = Mathf.Clamp01(elapsed / fadeSeconds);
                    fadeOverlay.color = overlayColor;
                    yield return null;
                }
            }

            // Keep the current tablet pose until the display has turned off, then return
            // to Desk through the reciprocal easing curve instead of snapping to it.
            // SetHomeLocation must run while the tablet view is active so its ordinary
            // camera-pose application does not bypass the return interpolation.
            SetHomeLocation(CatHomeLocation.Desk);
            catPresentationMode?.RefreshLookAtCameraImmediately();
            if (!ExitTabletCameraView(useReciprocalEaseOut: true))
            {
                StopTabletCameraTransition(restoreSavedState: true);
            }
            tabletPowerTransitionCoroutine = null;
        }

        private Image EnsureTabletPowerFadeOverlay()
        {
            if (tabletPowerFadeOverlay != null)
            {
                return tabletPowerFadeOverlay;
            }

            if (tabletDisplayCanvas == null)
            {
                return null;
            }

            RectTransform canvasTransform = tabletDisplayCanvas.GetComponent<RectTransform>();
            if (canvasTransform == null)
            {
                return null;
            }

            GameObject overlayObject = new GameObject(
                "RuntimeTabletPowerFadeOverlay",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            RectTransform overlayTransform = overlayObject.GetComponent<RectTransform>();
            overlayTransform.SetParent(canvasTransform, false);
            overlayTransform.anchorMin = Vector2.zero;
            overlayTransform.anchorMax = Vector2.one;
            overlayTransform.offsetMin = Vector2.zero;
            overlayTransform.offsetMax = Vector2.zero;
            overlayTransform.SetAsLastSibling();

            tabletPowerFadeOverlay = overlayObject.GetComponent<Image>();
            tabletPowerFadeOverlay.color = Color.clear;
            tabletPowerFadeOverlay.raycastTarget = true;
            overlayObject.SetActive(false);
            return tabletPowerFadeOverlay;
        }

        private void RefreshTabletButtonAvailability()
        {
            if (tabletButton == null)
            {
                return;
            }

            bool shouldBeVisible = CanOpenTabletFromButton();
            if (tabletButton.gameObject.activeSelf != shouldBeVisible)
            {
                tabletButton.gameObject.SetActive(shouldBeVisible);
            }
        }

        private bool CanOpenTabletFromButton()
        {
            if (isTabletCameraViewActive ||
                tabletCameraTransitionCoroutine != null ||
                conversationTimelineCoroutine != null ||
                CatPositionController.CurrentConversationLocation != CatHomeLocation.Desk ||
                chatUI == null ||
                chatUI.IsTyping ||
                chatUI.IsInDialogueMode ||
                chatUI.IsMenuInputBlocked)
            {
                return false;
            }

            InputField inputField = chatUI.chatInputField;
            return inputField != null &&
                   inputField.gameObject.activeInHierarchy &&
                   inputField.interactable;
        }

        private void SuppressConversationInputForTablet()
        {
            if (chatUI == null || chatUiSuppressedForTablet)
            {
                return;
            }

            chatUI.SetUiSuppressed(true);
            chatUiSuppressedForTablet = true;
        }

        private void RestoreConversationInputAfterTablet()
        {
            if (chatUI == null || !chatUiSuppressedForTablet)
            {
                return;
            }

            chatUI.SetUiSuppressed(false);
            chatUI.RestoreNormalConversationInputMode();
            chatUiSuppressedForTablet = false;
        }

        /// <summary>
        /// タブレット閲覧用の固定視点へ補間移動する。呼び出し前の姿勢と入力状態は、
        /// <see cref="ExitTabletCameraView"/> で復帰できるよう保存する。
        /// </summary>
        public bool EnterTabletCameraView()
        {
            if (tabletCameraPoint == null)
            {
                Debug.LogWarning("[OpenBetaTitle] Tablet Camera Point が未設定です。", this);
                return false;
            }

            if (startupCameraPivot == null)
            {
                GameObject pivotObject = GameObject.Find("CameraEventPivot");
                startupCameraPivot = pivotObject != null ? pivotObject.transform : null;
            }

            if (startupCameraPivot == null)
            {
                Debug.LogWarning("[OpenBetaTitle] CameraEventPivot が見つからないため、タブレットカメラへ遷移できません。", this);
                return false;
            }

            if (!hasSavedTabletCameraPose)
            {
                savedTabletCameraPosition = startupCameraPivot.position;
                savedTabletCameraRotation = startupCameraPivot.rotation;
                savedTabletCursorLockMode = Cursor.lockState;
                savedTabletCursorVisible = Cursor.visible;
                CaptureAndSuspendTabletCameraInput();
                hasSavedTabletCameraPose = true;
            }

            isTabletCameraViewActive = true;
            SuppressConversationInputForTablet();
            StartTabletCameraTransition(tabletCameraPoint.position, tabletCameraPoint.rotation, unlockCursorWhenFinished: true);
            return true;
        }

        /// <summary>
        /// タブレット閲覧前に保存した視点と通常視点操作へ復帰する。
        /// </summary>
        public bool ExitTabletCameraView()
        {
            return ExitTabletCameraView(useReciprocalEaseOut: false);
        }

        private bool ExitTabletCameraView(bool useReciprocalEaseOut)
        {
            if (!hasSavedTabletCameraPose || startupCameraPivot == null)
            {
                return false;
            }

            isTabletCameraViewActive = false;
            SetTabletDisplayPowered(false);
            StartTabletCameraTransition(
                savedTabletCameraPosition,
                savedTabletCameraRotation,
                unlockCursorWhenFinished: false,
                restoreInputWhenFinished: true,
                useReciprocalEaseOut: useReciprocalEaseOut);
            return true;
        }

        private void StartTabletCameraTransition(
            Vector3 destinationPosition,
            Quaternion destinationRotation,
            bool unlockCursorWhenFinished,
            bool restoreInputWhenFinished = false,
            bool useReciprocalEaseOut = false)
        {
            if (tabletCameraTransitionCoroutine != null)
            {
                StopCoroutine(tabletCameraTransitionCoroutine);
            }

            tabletCameraTransitionCoroutine = StartCoroutine(TabletCameraTransitionRoutine(
                destinationPosition,
                destinationRotation,
                unlockCursorWhenFinished,
                restoreInputWhenFinished,
                useReciprocalEaseOut));
        }

        private IEnumerator TabletCameraTransitionRoutine(
            Vector3 destinationPosition,
            Quaternion destinationRotation,
            bool unlockCursorWhenFinished,
            bool restoreInputWhenFinished,
            bool useReciprocalEaseOut)
        {
            Vector3 startPosition = startupCameraPivot.position;
            Quaternion startRotation = startupCameraPivot.rotation;
            float duration = Mathf.Max(0.01f, tabletCameraTransitionSeconds);
            float elapsed = 0f;

            while (elapsed < duration && startupCameraPivot != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalizedTime = Mathf.Clamp01(elapsed / duration);
                float progress = useReciprocalEaseOut
                    ? EvaluateReciprocalEaseOut(normalizedTime, tabletPowerReturnReciprocalStrength)
                    : Mathf.SmoothStep(0f, 1f, normalizedTime);
                startupCameraPivot.SetPositionAndRotation(
                    Vector3.LerpUnclamped(startPosition, destinationPosition, progress),
                    Quaternion.SlerpUnclamped(startRotation, destinationRotation, progress));
                yield return null;
            }

            if (startupCameraPivot != null)
            {
                startupCameraPivot.SetPositionAndRotation(destinationPosition, destinationRotation);
            }

            if (unlockCursorWhenFinished)
            {
                SetTabletDisplayPowered(true);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (restoreInputWhenFinished)
            {
                RestoreTabletCameraInput();
                RestoreConversationInputAfterTablet();
                hasSavedTabletCameraPose = false;
                catPresentationMode?.RefreshLookAtCameraImmediately();
            }

            tabletCameraTransitionCoroutine = null;
        }

        private static float EvaluateReciprocalEaseOut(float normalizedTime, float strength)
        {
            float time = Mathf.Clamp01(normalizedTime);
            float reciprocalStrength = Mathf.Max(0f, strength);
            if (reciprocalStrength <= Mathf.Epsilon)
            {
                return time;
            }

            // (1 + s)t / (1 + st): a normalized reciprocal curve that reaches both
            // endpoints exactly, moves promptly after the display turns off, and eases
            // into the Desk conversation camera without an end-frame snap.
            return (1f + reciprocalStrength) * time / (1f + reciprocalStrength * time);
        }

        private void CaptureAndSuspendTabletCameraInput()
        {
            Behaviour[] inputControllers = tabletCameraInputControllers ?? Array.Empty<Behaviour>();
            savedTabletInputControllerEnabledStates = new bool[inputControllers.Length];
            for (int i = 0; i < inputControllers.Length; i++)
            {
                Behaviour inputController = inputControllers[i];
                if (inputController == null || inputController == this)
                {
                    continue;
                }

                savedTabletInputControllerEnabledStates[i] = inputController.enabled;
                inputController.enabled = false;
            }
        }

        private void RestoreTabletCameraInput()
        {
            Behaviour[] inputControllers = tabletCameraInputControllers ?? Array.Empty<Behaviour>();
            for (int i = 0; i < inputControllers.Length; i++)
            {
                Behaviour inputController = inputControllers[i];
                if (inputController != null && inputController != this && i < savedTabletInputControllerEnabledStates.Length)
                {
                    inputController.enabled = savedTabletInputControllerEnabledStates[i];
                }
            }

            Cursor.lockState = savedTabletCursorLockMode;
            Cursor.visible = savedTabletCursorVisible;
            savedTabletInputControllerEnabledStates = Array.Empty<bool>();
        }

        private void StopTabletCameraTransition(bool restoreSavedState)
        {
            if (tabletCameraTransitionCoroutine != null)
            {
                StopCoroutine(tabletCameraTransitionCoroutine);
                tabletCameraTransitionCoroutine = null;
            }

            if (restoreSavedState && hasSavedTabletCameraPose)
            {
                if (startupCameraPivot != null)
                {
                    startupCameraPivot.SetPositionAndRotation(savedTabletCameraPosition, savedTabletCameraRotation);
                }

                RestoreTabletCameraInput();
                RestoreConversationInputAfterTablet();
                hasSavedTabletCameraPose = false;
            }

            SetTabletDisplayPowered(false);
            isTabletCameraViewActive = false;
        }

        private void SetTabletDisplayPowered(bool powered)
        {
            if (powered)
            {
                if (tabletDisplayCanvasGroup == null && tabletDisplayCanvas != null)
                {
                    tabletDisplayCanvasGroup = tabletDisplayCanvas.GetComponent<CanvasGroup>();
                }

                if (tabletPowerFadeOverlay != null)
                {
                    Color overlayColor = tabletPowerFadeOverlay.color;
                    overlayColor.a = 0f;
                    tabletPowerFadeOverlay.color = overlayColor;
                    tabletPowerFadeOverlay.gameObject.SetActive(false);
                }

                if (tabletDisplayCanvas != null && !tabletDisplayCanvas.activeSelf)
                {
                    tabletDisplayCanvas.SetActive(true);
                }

                // TabletDisplayController keeps its own power state and can leave the
                // shared CanvasGroup transparent. Active alone is therefore not a
                // display guarantee; restore the visible state at the single power-on boundary.
                if (tabletDisplayCanvasGroup != null)
                {
                    tabletDisplayCanvasGroup.alpha = 1f;
                    tabletDisplayCanvasGroup.interactable = true;
                    tabletDisplayCanvasGroup.blocksRaycasts = true;
                }

                if (tabletEmissionPanel != null && !tabletEmissionPanel.activeSelf)
                {
                    tabletEmissionPanel.SetActive(true);
                }

                return;
            }

            if (tabletDisplayCanvas != null && tabletDisplayCanvas.activeSelf)
            {
                tabletDisplayCanvas.SetActive(false);
            }

            if (tabletEmissionPanel != null && tabletEmissionPanel.activeSelf)
            {
                tabletEmissionPanel.SetActive(false);
            }
        }

        private void StopConversationTimelinePlayback(bool resumePresentation)
        {
            if (conversationTimelineCoroutine != null)
            {
                StopCoroutine(conversationTimelineCoroutine);
                conversationTimelineCoroutine = null;
            }

            if (activeConversationTimelineBinding != null)
            {
                activeConversationTimelineBinding.startDirector?.Stop();
                activeConversationTimelineBinding.endDirector?.Stop();
                activeConversationTimelineBinding = null;
            }

            if (isConversationFadeFlowActive)
            {
                ClearConversationFadeEventHandlers();
                activeConversationFadeController?.SetTransparent();
                isConversationFadeFlowActive = false;
                activeConversationFadeController = null;
            }

            if (resumePresentation)
            {
                catPresentationMode?.ResumeIdleAfterTimeline();
            }
        }

        /// <summary>
        /// Timeline Signal 用の場所遷移確定点です。Inspector で指定したシーンを読み込みます。
        /// 暗転完了後の Marker に割り当ててください。
        /// </summary>
        public void ApplyLocationChange()
        {
            LoadLocationScene(locationTransitionSceneName, locationTransitionLoadMode);
        }

        /// <summary>
        /// Signal Receiver の UnityEvent から任意のシーン名を指定して読み込みます。
        /// </summary>
        public void LoadLocationScene(string sceneName)
        {
            LoadLocationScene(sceneName, locationTransitionLoadMode);
        }

        /// <summary>
        /// Timeline Signal 用に既定のフェード時間で暗転を開始します。
        /// </summary>
        public void FadeToBlack()
        {
            if (isConversationFadeFlowActive)
            {
                return;
            }

            ResolveLocationTransitionFadeController()?.FadeToBlack(locationTransitionFadeSeconds);
        }

        /// <summary>
        /// Timeline Signal 用に既定のフェード時間で明転を開始します。
        /// </summary>
        public void FadeFromBlack()
        {
            if (isConversationFadeFlowActive)
            {
                return;
            }

            ResolveLocationTransitionFadeController()?.FadeFromBlack(locationTransitionFadeSeconds);
        }

        /// <summary>
        /// Timeline Signal 用に即時暗転します。ロード直前の安全策として使えます。
        /// </summary>
        public void SetTransitionOpaque()
        {
            ResolveLocationTransitionFadeController()?.SetOpaque();
        }

        /// <summary>
        /// Timeline Signal 用に即時明転します。
        /// </summary>
        public void SetTransitionTransparent()
        {
            ResolveLocationTransitionFadeController()?.SetTransparent();
        }

        private bool IsDeskHomeLocationActive()
        {
            return catPresentationMode != null &&
                   catPresentationMode.PositionController != null &&
                   catPresentationMode.PositionController.CurrentHomeLocation == CatHomeLocation.Desk;
        }

        private void LoadLocationScene(string sceneName, LoadSceneMode loadSceneMode)
        {
            if (locationSceneLoadRequested)
            {
                Debug.LogWarning("[OpenBetaTitle] 場所遷移のシーンロード要求が重複したため無視しました。", this);
                return;
            }

            string normalizedSceneName = sceneName?.Trim();
            if (string.IsNullOrEmpty(normalizedSceneName))
            {
                Debug.LogWarning("[OpenBetaTitle] 場所遷移先の Scene Name が未設定です。ApplyLocationChange は実行しません。", this);
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(normalizedSceneName))
            {
                Debug.LogWarning($"[OpenBetaTitle] 場所遷移先 '{normalizedSceneName}' を読み込めません。Build Settings に追加されているか確認してください。", this);
                return;
            }

            locationSceneLoadRequested = true;
            SceneManager.LoadScene(normalizedSceneName, loadSceneMode);
        }

        private FadeController ResolveLocationTransitionFadeController()
        {
            if (locationTransitionFadeController == null)
            {
                locationTransitionFadeController = GetComponent<FadeController>() ??
                    FindFirstObjectByType<FadeController>(FindObjectsInactive.Include);
            }

            if (locationTransitionFadeController == null)
            {
                locationTransitionFadeController = gameObject.AddComponent<FadeController>();
            }

            Canvas canvas = chatUI != null && chatUI.messageText != null
                ? chatUI.messageText.canvas
                : FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogWarning("[OpenBetaTitle] 暗転用 Canvas が見つかりません。", this);
                return null;
            }

            locationTransitionFadeController.Initialize(canvas);
            return locationTransitionFadeController;
        }

        private void ApplyHomeLocationCameraPose(CatHomeLocation location)
        {
            if (isTabletCameraViewActive)
            {
                return;
            }

            if (startupCameraPivot == null)
            {
                GameObject pivotObject = GameObject.Find("CameraEventPivot");
                startupCameraPivot = pivotObject != null ? pivotObject.transform : null;
            }

            if (startupCameraPivot == null)
            {
                return;
            }

            startupCameraPivot.localPosition = location == CatHomeLocation.Desk
                ? deskHomeCameraLocalPosition
                : postOpeningCameraLocalPosition;
            startupCameraPivot.localRotation = Quaternion.Euler(postOpeningCameraLocalEulerAngles);
        }

        private void EnsureBackgroundMusicControllerOnSelf()
        {
            if (this == null)
            {
                return;
            }

            if (backgroundMusicController != null)
            {
                return;
            }

            backgroundMusicController = GetComponent<BackgroundMusicController>();
            if (backgroundMusicController == null)
            {
                backgroundMusicController = gameObject.AddComponent<BackgroundMusicController>();
            }
        }

        private void PlayGameBgmNow()
        {
            EnsureBackgroundMusicControllerOnSelf();
            if (backgroundMusicController == null)
            {
                Debug.LogWarning("[OpenBetaTitle][Flow] Game BGM skipped: BackgroundMusicController could not be resolved.", this);
                return;
            }

            backgroundMusicController.PlayGameMusic(true);
        }

        private static OpenBetaInfoPanelController FindAboutPanelByName(string objectName)
        {
            OpenBetaInfoPanelController[] panels = Resources.FindObjectsOfTypeAll<OpenBetaInfoPanelController>();
            for (int i = 0; i < panels.Length; i++)
            {
                OpenBetaInfoPanelController panel = panels[i];
                if (panel != null && panel.gameObject.scene.IsValid() &&
                    string.Equals(panel.name, objectName, StringComparison.Ordinal))
                {
                    return panel;
                }
            }

            return null;
        }

        private void EnsureDiaryCalendarController()
        {
            if (GetComponent<DiaryCalendarController>() == null)
            {
                gameObject.AddComponent<DiaryCalendarController>();
            }
        }

        private void EnsureTabletHomeApplicationController()
        {
            if (GetComponent<TabletHomeApplicationController>() == null)
            {
                gameObject.AddComponent<TabletHomeApplicationController>();
            }
        }

        private static RectTransform FindSceneRectTransformByName(string objectName)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            RectTransform[] transforms = Resources.FindObjectsOfTypeAll<RectTransform>();
            for (int i = 0; i < transforms.Length; i++)
            {
                RectTransform candidate = transforms[i];
                if (candidate != null &&
                    candidate.gameObject.scene.IsValid() &&
                    candidate.gameObject.scene == activeScene &&
                    string.Equals(candidate.name, objectName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static T ResolveActiveSceneReference<T>(T current) where T : Component
        {
            if (IsInActiveScene(current))
            {
                return current;
            }

            T resolved = FindActiveSceneComponent<T>();
            return resolved != null ? resolved : current;
        }

        private static T FindActiveSceneComponent<T>() where T : Component
        {
            Scene activeScene = SceneManager.GetActiveScene();
            T[] components = Resources.FindObjectsOfTypeAll<T>();
            T fallback = null;
            for (int i = 0; i < components.Length; i++)
            {
                T component = components[i];
                if (IsInScene(component, activeScene))
                {
                    if (string.Equals(component.gameObject.name, "DialogueUI", StringComparison.Ordinal) ||
                        string.Equals(component.gameObject.name, typeof(T).Name, StringComparison.Ordinal))
                    {
                        return component;
                    }

                    fallback ??= component;
                }
            }

            return fallback;
        }

        private static bool IsInActiveScene(Component component)
        {
            return IsInScene(component, SceneManager.GetActiveScene());
        }

        private static bool IsInScene(Component component, Scene scene)
        {
            return component != null &&
                   component.gameObject != null &&
                   component.gameObject.scene.IsValid() &&
                   component.gameObject.scene == scene;
        }

        private static DialogueManager ResolveDialogueManagerReference(DialogueManager current)
        {
            if (DialogueManager.Instance != null)
            {
                return DialogueManager.Instance;
            }

            if (current != null)
            {
                return current;
            }

            return FindActiveSceneComponent<DialogueManager>() ??
                   FindFirstObjectByType<DialogueManager>(FindObjectsInactive.Include);
        }

        private void RebuildConversationSystem()
        {
            VerboseLog($"[OpenBetaTitle][Flow] RebuildConversationSystem before engine={(dialogueEngine != null)} manager={(dialogueManager != null)} chatUI={(chatUI != null)}");
            conversationDataManager?.Reload();

            dialogueEngine ??= DialogueEngine.Instance ?? FindFirstObjectByType<DialogueEngine>(FindObjectsInactive.Include);
            dialogueEngine?.RebuildRuntimeData(conversationDataManager);
            dialogueEngine?.ResetRuntimeConversationState();

            dialogueManager = ResolveDialogueManagerReference(dialogueManager);
            dialogueManager?.RebuildRuntimeReferences(chatUI, catData, false);
            dialogueManager?.ResetRuntimeStateForTitleRestart();
            VerboseLog($"[OpenBetaTitle][Flow] RebuildConversationSystem after engine={(dialogueEngine != null)} manager={(dialogueManager != null)} chatUI={(chatUI != null)}");
        }

        private void ResetRuntimeStateForFreshTitleSession()
        {
            VerboseLog($"[OpenBetaTitle][Flow] ResetRuntimeStateForFreshTitleSession chatUI={(chatUI != null)} manager={(dialogueManager != null)} engine={(dialogueEngine != null)}");
            chatUI = FindActiveSceneComponent<ChatUIController>();
            chatUI?.ResetRuntimeStateForTitleRestart(true);
            titleInputSubscriptionChatUI = null;
            titleInputSubscriptionApplied = false;
            EnsureTitleInputSubscription();
            if (chatUI != null)
            {
                chatUI.SetTitleInputRouter(this);
            }

            dialogueManager?.ResetRuntimeStateForTitleRestart();
            dialogueEngine?.ResetRuntimeConversationState();
        }

        private void PrepareSeparatedCatRuntime()
        {
            if (activeAnimationPattern == null)
            {
                return;
            }

            opCatRoot ??= activeAnimationPattern.RootObject;
            if (opCatRoot == null && activeAnimationPattern.CatAnimator != null)
            {
                opCatRoot = activeAnimationPattern.CatAnimator.gameObject;
            }

            EnsureNormalCatHomeAnchors();
            EnsureNormalCatRoot();

            if (catPresentationMode == null)
            {
                catPresentationMode = GetComponent<CatPresentationModeController>();
                if (catPresentationMode == null)
                {
                    catPresentationMode = gameObject.AddComponent<CatPresentationModeController>();
                }
            }

            catPresentationMode.Configure(
                opCatRoot,
                normalCatRoot,
                normalCatHomeAnchor,
                normalCatHomeDeskAnchor,
                chatUI);
            catPresentationMode.EnterOpeningMode();
        }

        private void EnsureNormalCatHomeAnchor()
        {
            if (normalCatHomeAnchor != null)
            {
                return;
            }

            Transform existing = transform.Find("Normal_Cat_Home");
            if (existing == null)
            {
                GameObject anchor = new GameObject("Normal_Cat_Home");
                anchor.transform.SetParent(transform, false);
                existing = anchor.transform;
            }

            Transform source = opCatRoot != null ? opCatRoot.transform : catAnimator != null ? catAnimator.transform : null;
            if (source != null)
            {
                existing.SetPositionAndRotation(source.position, source.rotation);
            }

            normalCatHomeAnchor = existing;
        }

        private void EnsureNormalCatHomeAnchors()
        {
            EnsureNormalCatHomeAnchor();
            if (normalCatHomeDeskAnchor != null)
            {
                return;
            }

            Transform existing = transform.Find("Normal_Cat_Home_Desk");
            if (existing == null)
            {
                GameObject anchor = new GameObject("Normal_Cat_Home_Desk");
                anchor.transform.SetParent(transform, false);
                existing = anchor.transform;
                existing.SetPositionAndRotation(normalCatHomeAnchor.position, normalCatHomeAnchor.rotation);
            }

            normalCatHomeDeskAnchor = existing;
        }

        private void EnsureNormalCatRoot()
        {
            if (normalCatRoot != null || !createNormalCatCloneAtRuntime || opCatRoot == null)
            {
                return;
            }

            normalCatRoot = Instantiate(opCatRoot, opCatRoot.transform.parent);
            normalCatRoot.name = "Normal_Cat";
            normalCatRoot.SetActive(false);

            CatPositionController positionController = normalCatRoot.GetComponent<CatPositionController>();
            if (positionController == null)
            {
                positionController = normalCatRoot.AddComponent<CatPositionController>();
            }

            positionController.SetHomeAnchors(normalCatHomeAnchor, normalCatHomeDeskAnchor);
            positionController.SetHomeLocation(CatHomeLocation.Table);
            positionController.SetHome(normalCatHomeAnchor.position, normalCatHomeAnchor.rotation);
            positionController.MoveToHome();
        }

        private void ActivateNormalCatForDialogue()
        {
            StopAlternateMoveEndIdleLoop();
            StopAlternateCatCallDirector(initialCatCallTimelineDirector);
            StopAlternateCatCallDirector(alternateMoveStartTimelineDirector);
            StopAlternateCatCallDirector(alternateMoveEndTimelineDirector);

            holdCatTransformAfterInitialCall = false;
            keepPostInitialCatCallState = false;
            heldCatPose = null;
            lastAlternateCompletedPose = null;
            alternateMoveStartCompletedPose = null;
            catHoldRoot = null;

            catPresentationMode?.EnterNormalMode();
            if (catPresentationMode != null && catPresentationMode.NormalAnimator != null)
            {
                catAnimator = catPresentationMode.NormalAnimator;
                catHoldRoot = catPresentationMode.PositionController != null
                    ? catPresentationMode.PositionController.transform
                    : catAnimator != null ? catAnimator.transform : null;
            }
        }

        private void EnsureYarnSystem()
        {
            DialogueRunner dialogueRunner = yarnManager != null ? yarnManager.DialogueRunner : FindFirstObjectByType<DialogueRunner>(FindObjectsInactive.Include);
            if (dialogueRunner == null)
            {
                GameObject dialogueSystem = GameObject.Find("DialogueSystem");
                if (dialogueSystem == null)
                {
                    dialogueSystem = new GameObject("DialogueSystem");
                }

                dialogueRunner = dialogueSystem.GetComponent<DialogueRunner>();
                if (dialogueRunner == null)
                {
                    dialogueRunner = dialogueSystem.AddComponent<DialogueRunner>();
                }
            }

            if (yarnProject != null && dialogueRunner.YarnProject == null)
            {
                dialogueRunner.SetProject(yarnProject);
            }

            if (yarnManager == null)
            {
                yarnManager = dialogueRunner.GetComponent<YarnManager>();
                if (yarnManager == null)
                {
                    yarnManager = dialogueRunner.gameObject.AddComponent<YarnManager>();
                }
            }

            if (yarnProject != null)
            {
                yarnManager.SetYarnProject(yarnProject);
            }
        }

        private void LoadSetupValues()
        {
            RenameSystem.Load(catData);
            playerName = SanitizeSetupValue(GetSavedSetupValue(RenameSystem.SaveKeyPlayerName, SaveKeyPlayerName));
            catName = SanitizeSetupValue(GetSavedSetupValue(RenameSystem.SaveKeyCatName, SaveKeyCatName, DefaultCatName));
            playerCalling = SanitizeSetupValue(GetSavedSetupValue(RenameSystem.SaveKeyPlayerCalling, SaveKeyPlayerCalling));
            catPronoun = SanitizeSetupValue(PlayerPrefs.GetString(RenameSystem.SaveKeyCatPronoun, DefaultCatPronoun));
            if (string.IsNullOrWhiteSpace(catPronoun))
            {
                catPronoun = DefaultCatPronoun;
            }
            ApplySetupValuesToRuntime();
        }

        private void SaveSetupValues()
        {
            playerName = SanitizeSetupValue(playerName);
            catName = SanitizeSetupValue(catName);
            playerCalling = SanitizeSetupValue(playerCalling);
            catPronoun = SanitizeSetupValue(catPronoun);
            PlayerPrefs.SetString(SaveKeyPlayerName, playerName);
            PlayerPrefs.SetString(SaveKeyCatName, catName);
            PlayerPrefs.SetString(SaveKeyPlayerCalling, playerCalling);
            PlayerPrefs.SetString(RenameSystem.SaveKeyCatPronoun, string.IsNullOrWhiteSpace(catPronoun) ? DefaultCatPronoun : catPronoun);
            if (catData != null)
            {
                catData.playerName = playerName;
                catData.catName = catName;
                catData.playerCalling = playerCalling;
                catData.catPronoun = string.IsNullOrWhiteSpace(catPronoun) ? DefaultCatPronoun : catPronoun;
                RenameSystem.Save(catData);
            }
            PlayerPrefs.Save();
            ApplySetupValuesToRuntime();
        }

        private static string GetSavedSetupValue(string primaryKey, string fallbackKey, string defaultValue = "")
        {
            if (!string.IsNullOrWhiteSpace(primaryKey) && PlayerPrefs.HasKey(primaryKey))
            {
                return PlayerPrefs.GetString(primaryKey, string.Empty);
            }

            if (!string.IsNullOrWhiteSpace(fallbackKey) && PlayerPrefs.HasKey(fallbackKey))
            {
                return PlayerPrefs.GetString(fallbackKey, string.Empty);
            }

            return defaultValue ?? string.Empty;
        }

        private static string SanitizeSetupValue(string value)
        {
            return value != null ? value.Trim() : string.Empty;
        }

        private void ApplySetupValuesToRuntime()
        {
            playerName = SanitizeSetupValue(playerName);
            catName = SanitizeSetupValue(catName);
            playerCalling = SanitizeSetupValue(playerCalling);
            catPronoun = SanitizeSetupValue(catPronoun);

            if (catData != null)
            {
                catData.playerName = playerName;
                catData.catName = catName;
                catData.playerCalling = playerCalling;
                catData.catPronoun = string.IsNullOrWhiteSpace(catPronoun) ? DefaultCatPronoun : catPronoun;
            }

            chatUI?.SetRuntimeRenameValues(playerName, catName, playerCalling, catPronoun);

            if (gameStateManager == null)
            {
                return;
            }

            gameStateManager.State.SetString("PlayerName", playerName);
            gameStateManager.State.SetString("CatName", catName);
            gameStateManager.State.SetString("PlayerCalling", playerCalling);
            gameStateManager.State.SetString("PLAYER_NAME", playerName);
            gameStateManager.State.SetString("CAT_NAME", catName);
            gameStateManager.State.SetString("PLAYER_CALLING", playerCalling);
            gameStateManager.State.SetString("CatPronoun", string.IsNullOrWhiteSpace(catPronoun) ? DefaultCatPronoun : catPronoun);
            gameStateManager.State.SetString("CAT_PRONOUN", string.IsNullOrWhiteSpace(catPronoun) ? DefaultCatPronoun : catPronoun);
        }

        private static bool ShouldHideQuitButtonInCurrentBuild()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }

        private void BindPanels()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TitlePanelDebug($"BindPanels:before character={FormatPanelDebug(characterSetupPanel)}");
#endif
            introPanel.Configure(
                Resolve(OpenBetaDialogueKeys.IntroBody),
                Resolve(OpenBetaDialogueKeys.StartDemoButton),
                Resolve(OpenBetaDialogueKeys.AboutButton),
                Resolve(OpenBetaDialogueKeys.QuitButton),
                StartNormalGame,
                ShowAbout,
                Application.Quit);
            introPanel.SetQuitButtonActive(!ShouldHideQuitButtonInCurrentBuild());
            introPanel.ConfigureDebugButton("Debug Start", StartDebugGame);

            introPanel.ConfigureAuxiliaryButtons(
                Resolve(OpenBetaDialogueKeys.SteamButton),
                Resolve(OpenBetaDialogueKeys.WorldviewButton),
                Resolve(OpenBetaDialogueKeys.TrainingDataButton),
                () => Application.OpenURL(string.IsNullOrWhiteSpace(steamUrl) ? DefaultSteamUrl : steamUrl),
                ShowWorldview,
                ShowTrainingData);
            introPanel.ConfigureCreditButton(ShowCredit);
            introPanel.SetPrecautions(Resolve(OpenBetaDialogueKeys.Precautions));

            aboutPanel.Configure(
                Resolve(OpenBetaDialogueKeys.AboutBody),
                Resolve(OpenBetaDialogueKeys.SteamButton),
                Resolve(OpenBetaDialogueKeys.BackButton),
                () => Application.OpenURL(string.IsNullOrWhiteSpace(steamUrl) ? DefaultSteamUrl : steamUrl),
                ShowIntro);

            worldviewPanel?.Configure(
                Resolve(OpenBetaDialogueKeys.Worldview),
                string.Empty,
                Resolve(OpenBetaDialogueKeys.BackButton),
                null,
                ShowIntro);

            trainingDataPanel?.Configure(
                Resolve(OpenBetaDialogueKeys.TrainingData),
                string.Empty,
                Resolve(OpenBetaDialogueKeys.BackButton),
                null,
                ShowIntro);
            trainingDataPanel?.ConfigureLogButton(
                Resolve(OpenBetaDialogueKeys.LogButton),
                () => chatUI?.OpenLogWindowFromExternalContext());
            chatUI?.SetLogButtonLabel(Resolve(OpenBetaDialogueKeys.LogButton));
            BindCreditPanel();

            characterSetupPanel.Configure(
                Resolve(OpenBetaDialogueKeys.CharacterSetupTitle),
                Resolve(OpenBetaDialogueKeys.PlayerNameLabel),
                Resolve(OpenBetaDialogueKeys.CatNameLabel),
                Resolve(OpenBetaDialogueKeys.PlayerCallingLabel),
                Resolve(OpenBetaDialogueKeys.CatFirstPersonLabel),
                Resolve(OpenBetaDialogueKeys.ContinueButton),
                Resolve(OpenBetaDialogueKeys.BackButton),
                Resolve(OpenBetaDialogueKeys.RequiredNotice),
                ShowIntro,
                HandleCharacterSetupSubmitted);

            callCatPanel.Configure(
                Resolve(OpenBetaDialogueKeys.CallCatBody),
                string.Empty,
                null);
#if UNITY_WEBGL && !UNITY_EDITOR
            TitlePanelDebug($"BindPanels:after character={FormatPanelDebug(characterSetupPanel)}");
#endif
        }

        private void ShowIntro()
        {
            SetOnlyPanelActive(introPanel);
        }

        private void ShowAbout()
        {
            SetOnlyPanelActive(aboutPanel);
        }

        private void ShowWorldview()
        {
            SetOnlyPanelActive(worldviewPanel);
        }

        private void ShowTrainingData()
        {
            SetOnlyPanelActive(trainingDataPanel);
        }

        private void ShowCredit()
        {
            SetOnlyPanelActive(creditPanel);
        }

        private void BindCreditPanel()
        {
            if (creditPanel == null)
            {
                return;
            }

            Transform backButtonTransform = FindDescendantByName(creditPanel, "BackButton");
            Button backButton = backButtonTransform != null ? backButtonTransform.GetComponent<Button>() : null;
            BindButton(backButton, ShowIntro);
        }

        private void ShowCharacterSetup()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TitlePanelDebug($"ShowCharacterSetup:before hideDialogue character={FormatPanelDebug(characterSetupPanel)} values playerLen={(playerName != null ? playerName.Length : 0)} catLen={(catName != null ? catName.Length : 0)} callingLen={(playerCalling != null ? playerCalling.Length : 0)} pronounLen={(catPronoun != null ? catPronoun.Length : 0)}");
#endif
            HideDialogueUi();
#if UNITY_WEBGL && !UNITY_EDITOR
            TitlePanelDebug($"ShowCharacterSetup:before SetOnlyPanelActive character={FormatPanelDebug(characterSetupPanel)}");
#endif
            SetOnlyPanelActive(characterSetupPanel);
#if UNITY_WEBGL && !UNITY_EDITOR
            TitlePanelDebug($"ShowCharacterSetup:after SetOnlyPanelActive character={FormatPanelDebug(characterSetupPanel)}");
#endif
            characterSetupPanel.SetValues(playerName, catName, playerCalling, catPronoun);
            characterSetupPanel.RefreshForVisiblePanel();
#if UNITY_WEBGL && !UNITY_EDITOR
            TitlePanelDebug($"ShowCharacterSetup:after SetValues character={FormatPanelDebug(characterSetupPanel)}");
#endif
        }

        private void StartNormalGame()
        {
            isDebugStart = false;
            ShowCharacterSetup();
        }

        private void StartDebugGame()
        {
            isDebugStart = true;
            // デバッグ開始は、前回確定した呼称をそのまま使い、命名画面を経由しない。
            // OPを再生しない場合も、最終フレームを評価して通常プレイ時と同じカメラ姿勢にする。
            PrepareDebugStartState();
            EvaluateDirectorAtFinalPose(timelineDirector);
            CompleteOpening();
        }

        private void ShowCallCatPrompt()
        {
            VerboseLog($"[OpenBetaTitle][Flow] ShowCallCatPrompt before activePattern={(activeAnimationPattern != null ? activeAnimationPattern.Label : "<null>")} chatUI={(chatUI != null)} callCatPanel={(callCatPanel != null)}");
            runtimeFlowAborted = false;
            catPresentationMode?.EnterOpeningMode();
            ApplyActiveAnimationPattern();
            holdCatTransformAfterInitialCall = false;
            keepPostInitialCatCallState = false;
            waitingForOpeningTimeline = false;
            waitingForInitialCatCallTimeline = false;
            initialCatCallCompletionStarted = false;
            openingDialogueStarted = false;
            waitingForInitialCatCall = true;
            callCatPanel.Configure(
                Resolve(OpenBetaDialogueKeys.CallCatBody),
                string.Empty,
                null);
            SetOnlyPanelActive(callCatPanel);
            if (selectedAnimationPattern == OpenBetaTitleAnimationPatternSelection.PatternB)
            {
            }
            else
            {
                PlayCatAnimatorState(preInitialCatCallStateName, false);
            }
            ShowDialogueInput();
            VerboseLog($"[OpenBetaTitle][Flow] ShowCallCatPrompt after waitingForInitialCatCall={waitingForInitialCatCall} route={(chatUI != null && chatUI.RouteSubmittedInputToDialogueEngine)} activePattern={(activeAnimationPattern != null ? activeAnimationPattern.Label : "<null>")} initialDirector={FormatDirectorDebug(initialCatCallTimelineDirector)}");
        }

        private void HandleCharacterSetupSubmitted(string newPlayerName, string newCatName, string newPlayerCalling, string newCatPronoun)
        {
            playerName = SanitizeSetupValue(newPlayerName);
            catName = SanitizeSetupValue(newCatName);
            playerCalling = SanitizeSetupValue(newPlayerCalling);
            catPronoun = string.IsNullOrWhiteSpace(newCatPronoun) ? DefaultCatPronoun : SanitizeSetupValue(newCatPronoun);
            SaveSetupValues();

            if (isDebugStart)
            {
                PrepareDebugStartState();
                CompleteOpening();
                return;
            }

            ShowCallCatPrompt();
        }

        private void HandleCatCalled(string input)
        {
            VerboseLog($"[OpenBetaTitle][Flow] HandleCatCalled input={FormatDebugInput(input)} waitingBefore={waitingForInitialCatCall} callCatPanelActive={(callCatPanel != null && callCatPanel.gameObject.activeSelf)} activePattern={(activeAnimationPattern != null ? activeAnimationPattern.Label : "<null>")}");
            string normalizedInput = JapaneseTextNormalizer.NormalizeNameCallToken(input);
            wasCalledCorrectly =
                CatNameCallMatcher.ContainsName(normalizedInput, JapaneseTextNormalizer.NormalizeNameCallToken(catName)) ||
                CatNameCallMatcher.IsNameCall(normalizedInput, JapaneseTextNormalizer.NormalizeNameCallToken(catPronoun));
            gameStateManager?.State.SetBool("WasCalledCorrectly", wasCalledCorrectly);
            PlayGameBgmNow();
            waitingForInitialCatCall = false;
            callCatPanel.gameObject.SetActive(false);
            HideDialogueUi();
            PlayInitialCatCallTimeline();
            VerboseLog($"[OpenBetaTitle][Flow] HandleCatCalled after wasCalledCorrectly={wasCalledCorrectly} waitingForInitialCatCallTimeline={waitingForInitialCatCallTimeline} openingDialogueStarted={openingDialogueStarted}");
        }

        private void PlayOpeningTimeline()
        {
            waitingForOpeningTimeline = true;
            if (timelineDirector == null || timelineDirector.playableAsset == null)
            {
                StartOpeningDialogue();
                return;
            }

            timelineDirector.time = 0d;
            ConfigureResilientTimelineDirector(timelineDirector);
            timelineDirector.Play();
        }

        private void PlayInitialCatCallTimeline()
        {
            ApplyActiveAnimationPattern();
            VerboseLog($"[OpenBetaTitle][Flow] PlayInitialCatCallTimeline pattern={(activeAnimationPattern != null ? activeAnimationPattern.Label : "<null>")} hasPlayable={(activeAnimationPattern != null && activeAnimationPattern.HasPlayableReference)} playInitial={(activeAnimationPattern != null && activeAnimationPattern.PlayInitialDirector)} initialDirector={FormatDirectorDebug(initialCatCallTimelineDirector)} moveStart={FormatDirectorDebug(alternateMoveStartTimelineDirector)} moveEnd={FormatDirectorDebug(alternateMoveEndTimelineDirector)}");
            if (activeAnimationPattern == null || !activeAnimationPattern.HasPlayableReference)
            {
                waitingForInitialCatCallTimeline = false;
                initialCatCallCompletionStarted = false;
                holdCatTransformAfterInitialCall = false;
                keepPostInitialCatCallState = false;
                PlayPostInitialCatCallState(true);
                StartCoroutine(PlayAlternateCatCallThenOpeningDialogueRoutine());
                VerboseLog("[OpenBetaTitle][Flow] PlayInitialCatCallTimeline fallback: no playable reference.");
                return;
            }

            if (activeAnimationPattern != null && !activeAnimationPattern.PlayInitialDirector)
            {
                waitingForInitialCatCallTimeline = false;
                initialCatCallCompletionStarted = false;
                holdCatTransformAfterInitialCall = false;
                keepPostInitialCatCallState = false;
                StopInitialCatCallDirectorWithoutRelease();
                StartCoroutine(PlayAlternateCatCallThenOpeningDialogueRoutine());
                VerboseLog("[OpenBetaTitle][Flow] PlayInitialCatCallTimeline fallback: PlayInitialDirector is false.");
                return;
            }

            if (initialCatCallTimelineDirector == null || initialCatCallTimelineDirector.playableAsset == null)
            {
                PlayPostInitialCatCallState(true);
                StartHoldingCatTransformAfterInitialCall(false);
                StartCoroutine(PlayAlternateCatCallThenOpeningDialogueRoutine());
                VerboseLog("[OpenBetaTitle][Flow] PlayInitialCatCallTimeline fallback: initial director missing.");
                return;
            }

            waitingForInitialCatCallTimeline = false;
            initialCatCallCompletionStarted = false;
            holdCatTransformAfterInitialCall = false;
            keepPostInitialCatCallState = false;
            catHoldRoot = null;
            initialCatCallTimelineDirector.enabled = true;
            initialCatCallTimelineDirector.extrapolationMode = DirectorWrapMode.Hold;
            ConfigureResilientTimelineDirector(initialCatCallTimelineDirector);
            initialCatCallTimelineDirector.Stop();
            initialCatCallTimelineDirector.time = 0d;
            waitingForInitialCatCallTimeline = true;
            initialCatCallTimelineDirector.Play();
            VerboseLog($"[OpenBetaTitle][Flow] Initial director Play called state={initialCatCallTimelineDirector.state} duration={initialCatCallTimelineDirector.duration}");

            if (initialCatCallMonitorCoroutine != null)
            {
                StopCoroutine(initialCatCallMonitorCoroutine);
            }

            initialCatCallMonitorCoroutine = StartCoroutine(MonitorInitialCatCallTimelineRoutine());
        }

        private void HandleInitialCatCallTimelineStopped(PlayableDirector director)
        {
            VerboseLog($"[OpenBetaTitle][Flow] HandleInitialCatCallTimelineStopped waiting={waitingForInitialCatCallTimeline} sameDirector={director == initialCatCallTimelineDirector} director={FormatDirectorDebug(director)}");
            if (!waitingForInitialCatCallTimeline || director != initialCatCallTimelineDirector)
            {
                return;
            }
            BeginInitialCatCallCompletion();
        }

        private IEnumerator MonitorInitialCatCallTimelineRoutine()
        {
            yield return null;

            while (waitingForInitialCatCallTimeline &&
                   !initialCatCallCompletionStarted &&
                   initialCatCallTimelineDirector != null)
            {
                double duration = initialCatCallTimelineDirector.duration;
                bool hasDuration = !double.IsNaN(duration) && !double.IsInfinity(duration) && duration > 0d;
                if (initialCatCallTimelineDirector.state != PlayState.Playing ||
                    (hasDuration && initialCatCallTimelineDirector.time >= duration - 0.02d))
                {
                    BeginInitialCatCallCompletion();
                    break;
                }

                yield return null;
            }

            initialCatCallMonitorCoroutine = null;
        }

        private void BeginInitialCatCallCompletion()
        {
            VerboseLog($"[OpenBetaTitle][Flow] BeginInitialCatCallCompletion started={initialCatCallCompletionStarted} waiting={waitingForInitialCatCallTimeline}");
            if (initialCatCallCompletionStarted)
            {
                return;
            }

            initialCatCallCompletionStarted = true;
            waitingForInitialCatCallTimeline = false;

            if (initialCatCallMonitorCoroutine != null)
            {
                StopCoroutine(initialCatCallMonitorCoroutine);
                initialCatCallMonitorCoroutine = null;
            }

            if (initialCatCallCompleteCoroutine != null)
            {
                StopCoroutine(initialCatCallCompleteCoroutine);
            }

            initialCatCallCompleteCoroutine = StartCoroutine(CompleteInitialCatCallRoutine());
        }

        private IEnumerator CompleteInitialCatCallRoutine()
        {
            VerboseLog("[OpenBetaTitle][Flow] CompleteInitialCatCallRoutine begin");
            HoldInitialCatCallTimelineAtFinalPose();
            ReleaseInitialCatCallTimelineControl();
            PlayPostInitialCatCallState(true);
            StartHoldingCatTransformAfterInitialCall(false);
            yield return null;
            PlayPostInitialCatCallState(true);
            yield return PlayAlternateCatCallSequenceRoutine();
            StartOpeningDialogue();
            initialCatCallCompleteCoroutine = null;
            VerboseLog("[OpenBetaTitle][Flow] CompleteInitialCatCallRoutine end");
        }

        private void HandleTimelineStopped(PlayableDirector director)
        {
            if (!waitingForOpeningTimeline || director != timelineDirector)
            {
                return;
            }

            CompleteOpeningTimeline();
        }

        private void CompleteOpeningTimeline()
        {
            if (!waitingForOpeningTimeline)
            {
                return;
            }

            waitingForOpeningTimeline = false;
            EvaluateDirectorAtFinalPose(timelineDirector);
            CompleteOpening();
        }

        public void OnOpeningTimelineFinished()
        {
            if (waitingForOpeningTimeline)
            {
                waitingForOpeningTimeline = false;
            }

            EvaluateDirectorAtFinalPose(timelineDirector);
            CompleteOpening();
        }

        // Both the Timeline path and Debug Start enter here so OP cleanup remains single-sourced.
        private void CompleteOpening()
        {
            waitingForOpeningTimeline = false;
            waitingForInitialCatCallTimeline = false;
            waitingForInitialCatCall = false;
            EvaluateDirectorAtFinalPose(timelineDirector);
            catPresentationMode?.OnOpeningTimelineFinished();
            ApplyPostOpeningCameraPose();
            StartOpeningDialogue();
            if (isDebugStart)
            {
                NekolposDebugPanel.EnsureVisibleForDebugStart();
            }
        }

        private static void PrepareDebugStartState()
        {
            TimeManager timeManager = FindFirstObjectByType<TimeManager>(FindObjectsInactive.Include);
            timeManager?.SetCurrentTime(1, DayPeriod.Morning);
        }

        private void StartOpeningDialogue()
        {
            if (chatUI == null)
            {
                chatUI = FindActiveSceneComponent<ChatUIController>();
            }

            VerboseLog($"[OpenBetaTitle][Flow] StartOpeningDialogue requested aborted={runtimeFlowAborted} started={openingDialogueStarted} chatUI={(chatUI != null)} wasCalledCorrectly={wasCalledCorrectly}");
            if (runtimeFlowAborted || openingDialogueStarted || chatUI == null)
            {
                VerboseLog("[OpenBetaTitle][Flow] StartOpeningDialogue ignored.");
                return;
            }

            openingDialogueStarted = true;
            HideAllPanels();
            ActivateNormalCatForDialogue();
            StartCoroutine(OpeningDialogueRoutine());
            VerboseLog("[OpenBetaTitle][Flow] OpeningDialogueRoutine started.");
        }

        private IEnumerator OpeningDialogueRoutine()
        {
            VerboseLog($"[OpenBetaTitle][Flow] OpeningDialogueRoutine begin chatUI={(chatUI != null)} route={(chatUI != null && chatUI.RouteSubmittedInputToDialogueEngine)}");
            if (runtimeFlowAborted)
            {
                VerboseLog("[OpenBetaTitle][Flow] OpeningDialogueRoutine aborted before UI activation.");
                yield break;
            }

            chatUI.SetLogButtonAllowedDuringConversation(false);
            chatUI.EnterDialogueModeInstant();
            RestoreHeldCatTransformNow();
            yield return null;
            if (runtimeFlowAborted)
            {
                VerboseLog("[OpenBetaTitle][Flow] OpeningDialogueRoutine aborted after first frame.");
                yield break;
            }

            RestoreHeldCatTransformNow();

            string openingMessage = Resolve(wasCalledCorrectly ? OpenBetaDialogueKeys.OpeningCorrect : OpenBetaDialogueKeys.OpeningIncorrect);
            if (string.IsNullOrWhiteSpace(openingMessage))
            {
                openingMessage = wasCalledCorrectly
                    ? $"お待たせ、{playerCalling}。何か用？"
                    : "ん？何か言った？";
                Debug.LogWarning("[OpenBetaTitle] 開始会話の本文が空だったため、フォールバック文を表示します。");
            }

            string speakerName = !string.IsNullOrWhiteSpace(catName) ? catName : "{{CAT_NAME}}";
            VerboseLog($"[OpenBetaTitle][Flow] Opening message resolved blank={string.IsNullOrWhiteSpace(openingMessage)} speakerBlank={string.IsNullOrWhiteSpace(speakerName)}");
            chatUI.ShowMessageInstant(speakerName, openingMessage);
            RestoreHeldCatTransformNow();
            StartCoroutine(ReinforceCatHoldForDialogueStartRoutine());
            yield return WaitForTyping();
            yield return WaitForAdvance();
            RestoreHeldCatTransformNow();
            catPresentationMode?.EnterNormalMode();
            if (catPresentationMode == null)
            {
                chatUI.RouteSubmittedInputToDialogueEngine = true;
                chatUI.SetLogButtonAllowedDuringConversation(true);
                chatUI.SetTalkTopicHintsAllowed(true);
                chatUI.EnterPlayerInputMode();
            }

            GameManager.Instance?.EnsureGameFlowStarted(suppressGreeting: true);
            StartCoroutine(ReinforceCatHoldForDialogueStartRoutine());
            VerboseLog("[OpenBetaTitle][Flow] OpeningDialogueRoutine end");
        }

        private IEnumerator ReinforceCatHoldForDialogueStartRoutine()
        {
            for (int i = 0; i < 3; i++)
            {
                yield return null;
                RestoreHeldCatTransformNow();
                yield return new WaitForEndOfFrame();
                RestoreHeldCatTransformNow();
            }
        }

        private void HideDialogueUi()
        {
            if (chatUI == null)
            {
                return;
            }

            chatUI.SetUiSuppressed(true);
        }

        private void ShowDialogueInput()
        {
            if (chatUI == null)
            {
                Debug.LogWarning("[OpenBetaTitle][Flow] ShowDialogueInput skipped: chatUI is null.");
                return;
            }

            EnsureTitleInputSubscription();
            chatUI.SetTitleInputRouter(this);
            chatUI.SetUiSuppressed(false);
            chatUI.SetInputLanguageMode(ChatUIController.InputLanguageMode.JapaneseKana);
            chatUI.RouteSubmittedInputToDialogueEngine = false;
            chatUI.SetLogButtonAllowedDuringConversation(false);
            chatUI.SetTalkTopicHintsAllowed(false);
            chatUI.EnterPlayerInputMode();
            VerboseLog($"[OpenBetaTitle][Flow] ShowDialogueInput route={chatUI.RouteSubmittedInputToDialogueEngine} suppressed={false}");
        }

        private void HandlePlayerInputSubmitted(string input)
        {
            VerboseLog($"[OpenBetaTitle][Flow] HandlePlayerInputSubmitted session={titleSessionId} current={IsCurrentTitleSession} waitingForInitialCatCall={waitingForInitialCatCall} input={FormatDebugInput(input)}");
            if (!IsCurrentTitleSession)
            {
                VerboseLog($"[OpenBetaTitle][Flow] HandlePlayerInputSubmitted ignored: stale session session={titleSessionId} active={activeTitleSessionId}.");
                return;
            }

            if (waitingForInitialCatCall)
            {
                HandleCatCalled(input);
            }
            else
            {
                VerboseLog("[OpenBetaTitle][Flow] HandlePlayerInputSubmitted ignored: waitingForInitialCatCall is false.");
            }
        }

        public static bool TryRoutePlayerInputFromChatUI(ChatUIController source, string input)
        {
            OpenBetaTitleBootstrap bootstrap = ResolveActiveTitleBootstrap(source);
            if (bootstrap == null ||
                !bootstrap.enabled ||
                !bootstrap.IsCurrentTitleSession ||
                !OpenBetaSceneNames.IsTitleLikeScene(SceneManager.GetActiveScene().name))
            {
                VerboseLog($"[OpenBetaTitle][Flow] TryRoutePlayerInputFromChatUI skipped active={(bootstrap != null)} current={(bootstrap != null && bootstrap.IsCurrentTitleSession)} waiting={(bootstrap != null && bootstrap.waitingForInitialCatCall)} input={FormatDebugInput(input)}");
                return false;
            }

            if (!bootstrap.waitingForInitialCatCall)
            {
                VerboseLog($"[OpenBetaTitle][Flow] TryRoutePlayerInputFromChatUI rejected: current bootstrap is not waiting. source={(source != null ? source.name : "<null>")} input={FormatDebugInput(input)}");
                return false;
            }

            if (source != null && bootstrap.chatUI != source)
            {
                bootstrap.chatUI = source;
                bootstrap.EnsureTitleInputSubscription();
            }

            VerboseLog($"[OpenBetaTitle][Flow] TryRoutePlayerInputFromChatUI accepted source={(source != null ? source.name : "<null>")} input={FormatDebugInput(input)}");
            bootstrap.HandlePlayerInputSubmitted(input);
            return true;
        }

        public bool TryRoutePlayerInputFromChatUIInstance(ChatUIController source, string input)
        {
            if (this == null ||
                !enabled ||
                !IsCurrentTitleSession ||
                !OpenBetaSceneNames.IsTitleLikeScene(SceneManager.GetActiveScene().name))
            {
                VerboseLog($"[OpenBetaTitle][Flow] TryRoutePlayerInputFromChatUIInstance skipped valid={this != null} current={(this != null && IsCurrentTitleSession)} input={FormatDebugInput(input)}");
                return false;
            }

            if (!waitingForInitialCatCall)
            {
                VerboseLog($"[OpenBetaTitle][Flow] TryRoutePlayerInputFromChatUIInstance rejected: waitingForInitialCatCall=false input={FormatDebugInput(input)}");
                return false;
            }

            if (source != null && chatUI != source)
            {
                chatUI = source;
                EnsureTitleInputSubscription();
                chatUI.SetTitleInputRouter(this);
            }

            VerboseLog($"[OpenBetaTitle][Flow] TryRoutePlayerInputFromChatUIInstance accepted source={(source != null ? source.name : "<null>")} input={FormatDebugInput(input)}");
            HandlePlayerInputSubmitted(input);
            return true;
        }

        private static OpenBetaTitleBootstrap ResolveActiveTitleBootstrap(ChatUIController source)
        {
            if (activeTitleBootstrap != null &&
                activeTitleBootstrap.gameObject != null &&
                activeTitleBootstrap.isActiveAndEnabled &&
                OpenBetaSceneNames.IsTitleLikeScene(SceneManager.GetActiveScene().name))
            {
                return activeTitleBootstrap;
            }

            OpenBetaTitleBootstrap[] bootstraps = FindObjectsByType<OpenBetaTitleBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            OpenBetaTitleBootstrap bestCandidate = null;
            for (int i = 0; i < bootstraps.Length; i++)
            {
                OpenBetaTitleBootstrap candidate = bootstraps[i];
                if (candidate == null ||
                    candidate.gameObject == null ||
                    !candidate.isActiveAndEnabled ||
                    !OpenBetaSceneNames.IsTitleLikeScene(SceneManager.GetActiveScene().name))
                {
                    continue;
                }

                if (bestCandidate == null || candidate.titleSessionId > bestCandidate.titleSessionId)
                {
                    bestCandidate = candidate;
                }
            }

            if (bestCandidate != null)
            {
                bestCandidate.TryClaimCurrentTitleSessionIfNeeded("ResolveActiveTitleBootstrap");
            }

            return bestCandidate;
        }

        private void EnsureTitleInputSubscription()
        {
            if (chatUI == null)
            {
                return;
            }

            if (!TryClaimCurrentTitleSessionIfNeeded("EnsureTitleInputSubscription"))
            {
                VerboseLog($"[OpenBetaTitle][Flow] EnsureTitleInputSubscription skipped: stale session session={titleSessionId} active={activeTitleSessionId}.");
                return;
            }

            if (!titleInputSubscriptionApplied || titleInputSubscriptionChatUI != chatUI)
            {
                chatUI.SetTitleInputRoutingHandlers(HandlePlayerInputSubmitted, HandleDialogueBackRequested);
                titleInputSubscriptionChatUI = chatUI;
                titleInputSubscriptionApplied = true;
            }

            if (waitingForInitialCatCall)
            {
                chatUI.SetTitleInputRouter(this);
            }
        }

        private static string FormatDebugInput(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "<empty>";
            }

            string preview = value.Length <= 4 ? value : value.Substring(0, 4) + "...";
            return $"len={value.Length}, preview='{preview}'";
        }

        private static string FormatDirectorDebug(PlayableDirector director)
        {
            if (director == null)
            {
                return "<null>";
            }

            string assetName = director.playableAsset != null ? director.playableAsset.name : "<no asset>";
            return $"{director.name}/{assetName}/enabled={director.enabled}/active={director.gameObject.activeInHierarchy}/state={director.state}/time={director.time:0.00}/duration={director.duration:0.00}";
        }

        private void RecoverTitleTimelinePlayback(string reason)
        {
            if (waitingForOpeningTimeline && timelineDirector != null)
            {
                CompleteOpeningTimelineIfReachedEnd();
                if (waitingForOpeningTimeline && timelineDirector.state != PlayState.Playing)
                {
                    ResumeDirectorFromCurrentTime(timelineDirector, $"Opening/{reason}");
                }
            }

            if (waitingForInitialCatCallTimeline && !initialCatCallCompletionStarted && initialCatCallTimelineDirector != null)
            {
                if (IsDirectorAtOrPastEnd(initialCatCallTimelineDirector))
                {
                    BeginInitialCatCallCompletion();
                }
                else if (initialCatCallTimelineDirector.state != PlayState.Playing)
                {
                    ResumeDirectorFromCurrentTime(initialCatCallTimelineDirector, $"InitialCatCall/{reason}");
                }
            }

            bool isAlternateSequenceActive = alternateCatCallCoroutine != null;
            bool isAlternateMoveEndIdleActive = alternateMoveEndIdleCoroutine != null;
            if (isAlternateSequenceActive)
            {
                RecoverAlternateDirectorPlayback(alternateMoveStartTimelineDirector, $"AlternateMoveStart/{reason}");
                RecoverAlternateDirectorPlayback(alternateMoveEndTimelineDirector, $"AlternateMoveEnd/{reason}");
            }
            else if (isAlternateMoveEndIdleActive)
            {
                RecoverAlternateDirectorPlayback(alternateMoveEndTimelineDirector, $"AlternateMoveEndIdle/{reason}");
            }
        }

        private void RecoverAlternateDirectorPlayback(PlayableDirector director, string label)
        {
            if (director == null ||
                director.playableAsset == null ||
                !director.enabled ||
                !director.gameObject.activeInHierarchy ||
                IsDirectorAtOrPastEnd(director))
            {
                return;
            }

            if (director.state != PlayState.Playing)
            {
                ResumeDirectorFromCurrentTime(director, label);
            }
        }

        private static void ConfigureResilientTimelineDirector(PlayableDirector director)
        {
            if (director == null)
            {
                return;
            }

            director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
        }

        private static void ResumeDirectorFromCurrentTime(PlayableDirector director, string label)
        {
            if (director == null || director.playableAsset == null || !director.gameObject.activeInHierarchy)
            {
                return;
            }

            if (IsDirectorAtOrPastEnd(director))
            {
                EvaluateDirectorAtFinalPose(director);
                return;
            }

            double resumeTime = ClampDirectorTime(director, director.time);
            director.enabled = true;
            ConfigureResilientTimelineDirector(director);
            director.time = resumeTime;
            director.Play();
            director.time = resumeTime;
            director.Evaluate();
            VerboseLog($"[OpenBetaTitle][Flow] Resumed Timeline {label}: {FormatDirectorDebug(director)}");
        }

        private static bool IsDirectorAtOrPastEnd(PlayableDirector director)
        {
            if (director == null)
            {
                return false;
            }

            double duration = director.duration;
            return !double.IsNaN(duration) &&
                   !double.IsInfinity(duration) &&
                   duration > 0d &&
                   director.time >= duration - 0.02d;
        }

        private static void EvaluateDirectorAtFinalPose(PlayableDirector director)
        {
            if (director == null || director.playableAsset == null)
            {
                return;
            }

            director.enabled = true;
            director.time = ResolveTimelineFinalEvaluationTime(director);
            director.Evaluate();
        }

        private static double ClampDirectorTime(PlayableDirector director, double time)
        {
            if (director == null)
            {
                return 0d;
            }

            double duration = director.duration;
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0d)
            {
                return Math.Max(0d, time);
            }

            return Math.Max(0d, Math.Min(time, ResolveTimelineFinalEvaluationTime(director)));
        }

        private void HandleDialogueBackRequested()
        {
            waitingForOpeningTimeline = false;
            waitingForInitialCatCallTimeline = false;
            initialCatCallCompletionStarted = false;
            holdCatTransformAfterInitialCall = false;
            keepPostInitialCatCallState = false;
            heldCatPose = null;
            openingDialogueStarted = false;
            waitingForInitialCatCall = false;
            if (timelineDirector != null)
            {
                timelineDirector.Stop();
                timelineDirector.time = 0d;
            }

            if (initialCatCallTimelineDirector != null)
            {
                initialCatCallTimelineDirector.extrapolationMode = DirectorWrapMode.None;
                initialCatCallTimelineDirector.Stop();
                initialCatCallTimelineDirector.time = 0d;
                initialCatCallTimelineDirector.extrapolationMode = DirectorWrapMode.Hold;
            }

            StopAlternateCatCallSequence();

            if (initialCatCallCompleteCoroutine != null)
            {
                StopCoroutine(initialCatCallCompleteCoroutine);
                initialCatCallCompleteCoroutine = null;
            }

            if (initialCatCallMonitorCoroutine != null)
            {
                StopCoroutine(initialCatCallMonitorCoroutine);
                initialCatCallMonitorCoroutine = null;
            }

            ShowCharacterSetup();
        }

        private IEnumerator PlayAlternateCatCallSequenceRoutine()
        {
            if (alternateCatCallCoroutine != null)
            {
                yield break;
            }
            alternateCatCallCoroutine = StartCoroutine(PlayAlternateCatCallDirectorsRoutine());
            yield return alternateCatCallCoroutine;
            alternateCatCallCoroutine = null;
        }

        private IEnumerator PlayAlternateCatCallThenOpeningDialogueRoutine()
        {
            VerboseLog($"[OpenBetaTitle][Flow] PlayAlternateCatCallThenOpeningDialogueRoutine begin aborted={runtimeFlowAborted}");
            if (runtimeFlowAborted)
            {
                yield break;
            }

            yield return PlayAlternateCatCallSequenceRoutine();
            if (runtimeFlowAborted)
            {
                VerboseLog("[OpenBetaTitle][Flow] PlayAlternateCatCallThenOpeningDialogueRoutine aborted after sequence.");
                yield break;
            }

            yield return WaitForPostInitialCatCallStateReady();
            if (runtimeFlowAborted)
            {
                VerboseLog("[OpenBetaTitle][Flow] PlayAlternateCatCallThenOpeningDialogueRoutine aborted after state wait.");
                yield break;
            }

            StartHoldingCatTransformAfterInitialCall(true);
            RestoreHeldCatTransformNow();
            VerboseLog("[OpenBetaTitle][Flow] PlayAlternateCatCallThenOpeningDialogueRoutine starting opening dialogue.");
            StartOpeningDialogue();
        }

        private IEnumerator WaitForPostInitialCatCallStateReady()
        {
            if (catAnimator == null || string.IsNullOrWhiteSpace(postInitialCatCallStateName) || !CanPlayAnimator(catAnimator))
            {
                yield break;
            }

            float timeoutSeconds = Mathf.Max(0.1f, postInitialCatCallBlendSeconds + 0.15f);
            float deadline = Time.time + timeoutSeconds;
            while (Time.time < deadline && !IsPostInitialCatCallStateSettled())
            {
                RestoreHeldCatTransformNow();
                yield return null;
            }
        }

        private bool IsPostInitialCatCallStateSettled()
        {
            if (catAnimator == null || string.IsNullOrWhiteSpace(postInitialCatCallStateName) || !CanPlayAnimator(catAnimator))
            {
                return true;
            }

            AnimatorStateInfo currentState = catAnimator.GetCurrentAnimatorStateInfo(0);
            return currentState.IsName(postInitialCatCallStateName) && !catAnimator.IsInTransition(0);
        }

        private IEnumerator PlayAlternateCatCallDirectorsRoutine()
        {
            bool playedAny = false;
            VerboseLog($"[OpenBetaTitle][Flow] PlayAlternateCatCallDirectorsRoutine begin moveStart={FormatDirectorDebug(alternateMoveStartTimelineDirector)} moveEnd={FormatDirectorDebug(alternateMoveEndTimelineDirector)}");
            ReleaseHeldCatPoseForAlternateCatCall();
            if (alternateMoveStartTimelineDirector != null)
            {
                playedAny = true;
                yield return PlayAlternateCatCallDirectorRoutine(alternateMoveStartTimelineDirector, "MoveStart");
            }

            if (alternateMoveEndTimelineDirector != null)
            {
                playedAny = true;
                yield return PlayAlternateCatCallDirectorRoutine(alternateMoveEndTimelineDirector, "MoveEnd");
            }

            if (!playedAny)
            {
                VerboseLog("[OpenBetaTitle][Flow] PlayAlternateCatCallDirectorsRoutine no directors; continuing to dialogue.");
            }
            else
            {
                ApplyLastAlternateCompletedPose("before hold");
                bool keepMoveEndPlaying = ShouldKeepAlternateMoveEndPlayingAsIdle();
                StartHoldingCatTransformAfterInitialCall(!keepMoveEndPlaying);
                ApplyLastAlternateCompletedPose("after hold capture");
                if (keepMoveEndPlaying)
                {
                    StartAlternateMoveEndIdleLoop();
                }
            }
            VerboseLog($"[OpenBetaTitle][Flow] PlayAlternateCatCallDirectorsRoutine end playedAny={playedAny}");
        }

        private void ReleaseHeldCatPoseForAlternateCatCall()
        {
            CacheAlternateArmCatRestPose();
            holdCatTransformAfterInitialCall = false;
            keepPostInitialCatCallState = false;
            heldCatPose = null;
            lastAlternateCompletedPose = null;
            alternateMoveStartCompletedPose = null;
            StopAlternateMoveEndIdleLoop();

            if (catAnimator != null)
            {
                catAnimator.enabled = true;
                catAnimator.speed = 1f;
            }
        }

        private IEnumerator PlayAlternateCatCallDirectorRoutine(PlayableDirector director, string label)
        {
            if (director == null || director.playableAsset == null)
            {
                VerboseLog($"[OpenBetaTitle][Flow] Alternate {label} skipped: director or asset missing.");
                yield break;
            }

            if (!director.gameObject.activeInHierarchy)
            {
                VerboseLog($"[OpenBetaTitle][Flow] Alternate {label} skipped: director inactive. director={FormatDirectorDebug(director)}");
                yield break;
            }

            bool preserveRootTransform = ShouldPreserveAlternateRootTransform(label);
            Transform preservedRoot = preserveRootTransform ? GetCatHoldRoot() : null;
            Vector3 preservedPosition = preservedRoot != null ? preservedRoot.position : Vector3.zero;
            Quaternion preservedRotation = preservedRoot != null ? preservedRoot.rotation : Quaternion.identity;

            director.enabled = true;
            director.extrapolationMode = ShouldKeepAlternateDirectorPlayingAsIdle(label)
                ? DirectorWrapMode.Loop
                : DirectorWrapMode.Hold;
            ConfigureResilientTimelineDirector(director);
            director.Stop();
            director.time = 0d;
            RestoreAlternateNeckRestPose($"{label} before Play");
            director.Play();
            VerboseLog($"[OpenBetaTitle][Flow] Alternate {label} Play called state={director.state} duration={director.duration} time={director.time}");
            RestorePreservedRootTransform(preservedRoot, preserveRootTransform, preservedPosition, preservedRotation, label, "after Play");
            RestoreAlternateNeckRestPose($"{label} after Play");

            yield return null;
            RestorePreservedRootTransform(preservedRoot, preserveRootTransform, preservedPosition, preservedRotation, label, "after first frame");
            RestoreAlternateNeckRestPose($"{label} after first frame");
            if (director != null && director.state != PlayState.Playing)
            {
                VerboseLog($"[OpenBetaTitle][Flow] Alternate {label} did not enter Playing; continuing. state={director.state}");
            }

            float timeoutAt = Time.unscaledTime + Mathf.Clamp((float)(director.duration <= 0d || double.IsNaN(director.duration) || double.IsInfinity(director.duration) ? 2d : director.duration + 2d), 2f, 20f);
            while (director != null)
            {
                double duration = director.duration;
                bool hasDuration = !double.IsNaN(duration) && !double.IsInfinity(duration) && duration > 0d;
                if (hasDuration && director.time >= duration - 0.02d)
                {
                    break;
                }

                if (director.state != PlayState.Playing)
                {
                    ResumeDirectorFromCurrentTime(director, $"Alternate {label}");
                }

                if (Time.unscaledTime >= timeoutAt)
                {
                    Debug.LogWarning($"[OpenBetaTitle][Flow] Alternate {label} timed out; forcing dialogue fallback. state={director.state} time={director.time} duration={director.duration}");
                    break;
                }

                yield return null;
                RestorePreservedRootTransform(preservedRoot, preserveRootTransform, preservedPosition, preservedRotation, label, "during play");
                RestoreAlternateNeckRestPose($"{label} during play");
            }

            RestorePreservedRootTransform(preservedRoot, preserveRootTransform, preservedPosition, preservedRotation, label, "complete");
            RestoreAlternateNeckRestPose($"{label} complete");
            bool restoreCompletedRootAfterRelease = ShouldRestoreCompletedRootAfterRelease(label);
            Transform completedRoot = restoreCompletedRootAfterRelease ? GetCatHoldRoot() : null;
            Vector3 completedRootPosition = completedRoot != null ? completedRoot.position : Vector3.zero;
            Quaternion completedRootRotation = completedRoot != null ? completedRoot.rotation : Quaternion.identity;
            TransformSnapshot[] completedPose = CaptureCatPose(GetCatHoldRoot());
            lastAlternateCompletedPose = completedPose;
            if (string.Equals(label, "MoveStart", StringComparison.Ordinal))
            {
                alternateMoveStartCompletedPose = completedPose;
            }

            if (ShouldKeepAlternateDirectorPlayingAsIdle(label))
            {
                RestoreCompletedRootAfterRelease(completedRoot, restoreCompletedRootAfterRelease, completedRootPosition, completedRootRotation, label);
            }
            else
            {
                ReleaseCompletedAlternateDirector(director, label);
                RestoreCompletedRootAfterRelease(completedRoot, restoreCompletedRootAfterRelease, completedRootPosition, completedRootRotation, label);
                ApplyCatPoseSnapshot(completedPose);
            }
        }

        private void ReleaseCompletedAlternateDirector(PlayableDirector director, string label)
        {
            if (director == null)
            {
                return;
            }

            director.extrapolationMode = DirectorWrapMode.None;
            director.Stop();
            director.time = 0d;
            director.enabled = false;
            director.extrapolationMode = DirectorWrapMode.Hold;
        }

        private void StartAlternateMoveEndIdleLoop()
        {
            StopAlternateMoveEndIdleLoop();
            alternateMoveEndIdleCoroutine = StartCoroutine(AlternateMoveEndIdleLoopRoutine());
        }

        private void StopAlternateMoveEndIdleLoop()
        {
            if (alternateMoveEndIdleCoroutine == null)
            {
                return;
            }

            StopCoroutine(alternateMoveEndIdleCoroutine);
            alternateMoveEndIdleCoroutine = null;
        }

        private IEnumerator AlternateMoveEndIdleLoopRoutine()
        {
            PlayableDirector director = alternateMoveEndTimelineDirector;
            if (director == null || director.playableAsset == null)
            {
                alternateMoveEndIdleCoroutine = null;
                yield break;
            }
            while (director != null && director.playableAsset != null && ShouldKeepAlternateMoveEndPlayingAsIdle())
            {
                double duration = director.duration;
                bool hasDuration = !double.IsNaN(duration) && !double.IsInfinity(duration) && duration > 0d;
                if (director.state != PlayState.Playing)
                {
                    director.enabled = true;
                    director.extrapolationMode = DirectorWrapMode.Loop;
                    ConfigureResilientTimelineDirector(director);
                    director.Play();
                }
                else if (hasDuration && director.time >= duration - 0.01d)
                {
                    director.time = 0d;
                    director.Evaluate();
                }

                yield return null;
            }

            alternateMoveEndIdleCoroutine = null;
        }

        private void CacheAlternateNeckRestPose()
        {
            alternateNeckBone = catAnimator != null ? FindDescendantByName(catAnimator.transform, "neck") : null;
            hasAlternateNeckRestLocalRotation = alternateNeckBone != null;
            alternateNeckRestLocalRotation = hasAlternateNeckRestLocalRotation ? alternateNeckBone.localRotation : Quaternion.identity;
        }

        private void CacheAlternateArmCatRestPose()
        {
            alternateArmCat = null;
            hasAlternateArmCatRestPose = false;

            if (!IsActivePatternB())
            {
                return;
            }

            Transform root = GetCatHoldRoot();
            alternateArmCat = root != null ? FindDescendantByName(root, "Arm_Cat") : null;
            if (alternateArmCat == null && catAnimator != null)
            {
                alternateArmCat = FindDescendantByName(catAnimator.transform, "Arm_Cat");
            }

            if (alternateArmCat == null)
            {
                return;
            }

            alternateArmCatRestPose = new TransformSnapshot(alternateArmCat);
            hasAlternateArmCatRestPose = true;
        }

        private void RestoreAlternateArmCatRestPose(string phase)
        {
            if (!IsActivePatternB() || !hasAlternateArmCatRestPose)
            {
                return;
            }

            if (alternateArmCat == null)
            {
                hasAlternateArmCatRestPose = false;
                return;
            }

            alternateArmCatRestPose.Apply();
        }

        private void RestoreAlternateNeckRestPose(string phase)
        {
            if (!IsActivePatternB())
            {
                return;
            }

            if (!hasAlternateNeckRestLocalRotation || alternateNeckBone == null)
            {
                CacheAlternateNeckRestPose();
            }

            if (!hasAlternateNeckRestLocalRotation || alternateNeckBone == null)
            {
                return;
            }

            float angle = Quaternion.Angle(alternateNeckBone.localRotation, alternateNeckRestLocalRotation);
            if (angle <= 0.01f)
            {
                return;
            }

            alternateNeckBone.localRotation = alternateNeckRestLocalRotation;
        }

        private bool IsActivePatternB()
        {
            return activeAnimationPattern != null && ReferenceEquals(activeAnimationPattern, patternB);
        }

        private static Transform FindDescendantByName(Transform parent, string name)
        {
            if (parent == null)
            {
                return null;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child;
                }

                Transform result = FindDescendantByName(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private bool ShouldPreserveAlternateRootTransform(string label)
        {
            if (activeAnimationPattern == null || !activeAnimationPattern.PreserveMoveEndRootTransform)
            {
                return false;
            }

            return string.Equals(label, "MoveEnd", StringComparison.Ordinal);
        }

        private bool ShouldRestoreCompletedRootAfterRelease(string label)
        {
            if (activeAnimationPattern == null || !activeAnimationPattern.PreserveMoveEndRootTransform)
            {
                return false;
            }

            return string.Equals(label, "MoveEnd", StringComparison.Ordinal) ||
                   (string.Equals(label, "MoveStart", StringComparison.Ordinal) && alternateMoveEndTimelineDirector == null);
        }

        private bool ShouldKeepAlternateMoveEndPlayingAsIdle()
        {
            return IsActivePatternB() &&
                   alternateMoveEndTimelineDirector != null &&
                   alternateMoveEndTimelineDirector.playableAsset != null;
        }

        private bool ShouldKeepAlternateDirectorPlayingAsIdle(string label)
        {
            return string.Equals(label, "MoveEnd", StringComparison.Ordinal) &&
                   ShouldKeepAlternateMoveEndPlayingAsIdle();
        }

        private void RestoreCompletedRootAfterRelease(Transform root, bool enabled, Vector3 position, Quaternion rotation, string label)
        {
            if (!enabled || root == null)
            {
                return;
            }

            root.SetPositionAndRotation(position, rotation);
            heldCatWorldPosition = position;
            heldCatWorldRotation = rotation;
        }

        private void StopInitialCatCallDirectorWithoutRelease()
        {
            if (initialCatCallTimelineDirector == null)
            {
                return;
            }

            initialCatCallTimelineDirector.stopped -= HandleInitialCatCallTimelineStopped;
            initialCatCallTimelineDirector.Stop();
            initialCatCallTimelineDirector.time = 0d;
            initialCatCallTimelineDirector.enabled = false;
            initialCatCallTimelineDirector.stopped += HandleInitialCatCallTimelineStopped;
        }

        private static void RestorePreservedRootTransform(Transform root, bool enabled, Vector3 position, Quaternion rotation, string label, string phase)
        {
            if (!enabled || root == null)
            {
                return;
            }

            root.SetPositionAndRotation(position, rotation);
        }

        private void StopAlternateCatCallSequence()
        {
            StopAlternateMoveEndIdleLoop();
            if (alternateCatCallCoroutine != null)
            {
                StopCoroutine(alternateCatCallCoroutine);
                alternateCatCallCoroutine = null;
            }

            StopAlternateCatCallDirector(alternateMoveStartTimelineDirector);
            StopAlternateCatCallDirector(alternateMoveEndTimelineDirector);
        }

        private static void StopAlternateCatCallDirector(PlayableDirector director)
        {
            if (director == null)
            {
                return;
            }

            director.Stop();
            director.time = 0d;
        }

        private static void PrepareAlternateCatCallDirector(PlayableDirector director)
        {
            if (director == null)
            {
                return;
            }

            director.extrapolationMode = DirectorWrapMode.Hold;
            director.Stop();
            director.time = 0d;
        }

        private void ResolveAnimationPatterns()
        {
            patternA ??= new OpenBetaTitleAnimationPattern("A");
            patternB ??= new OpenBetaTitleAnimationPattern("B");

            ResolveAnimationPattern(
                patternA,
                playInitialDirectorDefault: true,
                preserveMoveEndRootDefault: false);
            ResolveAnimationPattern(
                patternB,
                playInitialDirectorDefault: false,
                preserveMoveEndRootDefault: true);

            activeAnimationPattern = SelectAnimationPattern();
        }

        private void ResolveAnimationPattern(
            OpenBetaTitleAnimationPattern pattern,
            bool playInitialDirectorDefault,
            bool preserveMoveEndRootDefault)
        {
            if (pattern == null)
            {
                return;
            }

            pattern.PlayInitialDirector = pattern.HasAnyReference ? pattern.PlayInitialDirector : playInitialDirectorDefault;
            pattern.PreserveMoveEndRootTransform = pattern.HasAnyReference ? pattern.PreserveMoveEndRootTransform : preserveMoveEndRootDefault;

            PrepareAlternateCatCallDirector(pattern.MoveStartDirector);
            PrepareAlternateCatCallDirector(pattern.MoveEndDirector);

            if (pattern.RootObject == null || pattern.CatAnimator == null)
            {
            }
            else
            {
            }
        }

        private OpenBetaTitleAnimationPattern SelectAnimationPattern()
        {
            if (selectedAnimationPattern == OpenBetaTitleAnimationPatternSelection.PatternA)
            {
                return patternA;
            }

            if (selectedAnimationPattern == OpenBetaTitleAnimationPatternSelection.PatternB)
            {
                return patternB;
            }

            bool aActive = patternA != null && patternA.IsRootActive;
            bool bActive = patternB != null && patternB.IsRootActive;

            if (bActive && !aActive)
            {
                return patternB;
            }

            if (aActive && !bActive)
            {
                return patternA;
            }

            if (bActive)
            {
                return patternB;
            }

            if (aActive)
            {
                return patternA;
            }
            return null;
        }

        private void ApplyActiveAnimationPattern()
        {
            if (activeAnimationPattern == null)
            {
                initialCatCallTimelineDirector = null;
                alternateMoveStartTimelineDirector = null;
                alternateMoveEndTimelineDirector = null;
                catAnimator = null;
                catHoldRoot = null;
                return;
            }

            catAnimator = activeAnimationPattern.CatAnimator;
            initialCatCallTimelineDirector = activeAnimationPattern.InitialDirector;
            alternateMoveStartTimelineDirector = activeAnimationPattern.MoveStartDirector;
            alternateMoveEndTimelineDirector = activeAnimationPattern.MoveEndDirector;
            catHoldRoot = ResolveCatHoldRoot(initialCatCallTimelineDirector, catAnimator);
            CacheAlternateArmCatRestPose();

            StopInactivePatternDirectors(activeAnimationPattern == patternA ? patternB : patternA);
        }

        private static void StopInactivePatternDirectors(OpenBetaTitleAnimationPattern pattern)
        {
            if (pattern == null)
            {
                return;
            }

            StopAlternateCatCallDirector(pattern.InitialDirector);
            StopAlternateCatCallDirector(pattern.MoveStartDirector);
            StopAlternateCatCallDirector(pattern.MoveEndDirector);
        }

        private static PlayableDirector FindPlayableDirectorByAssetName(string assetName)
        {
            PlayableDirector[] directors = FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (PlayableDirector director in directors)
            {
                if (director != null && director.playableAsset != null && string.Equals(director.playableAsset.name, assetName, StringComparison.Ordinal))
                {
                    return director;
                }
            }

            return null;
        }

        private static Animator FindAnimatorBoundToDirector(PlayableDirector director)
        {
            if (director == null || director.playableAsset == null)
            {
                return null;
            }

            foreach (PlayableBinding output in director.playableAsset.outputs)
            {
                UnityEngine.Object binding = director.GetGenericBinding(output.sourceObject);
                if (binding is Animator animator)
                {
                    return animator;
                }

                if (binding is GameObject gameObject && gameObject.TryGetComponent(out Animator gameObjectAnimator))
                {
                    return gameObjectAnimator;
                }

                if (binding is GameObject boundGameObject)
                {
                    Animator childAnimator = boundGameObject.GetComponentInChildren<Animator>(true);
                    if (childAnimator != null)
                    {
                        return childAnimator;
                    }
                }

                if (binding is Component component && component.TryGetComponent(out Animator componentAnimator))
                {
                    return componentAnimator;
                }

                if (binding is Component boundComponent)
                {
                    Animator childAnimator = boundComponent.GetComponentInChildren<Animator>(true);
                    if (childAnimator != null)
                    {
                        return childAnimator;
                    }
                }
            }

            return null;
        }

        private static bool IsPatternBPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   (path.Contains("B Cat_Collar_C1", StringComparison.Ordinal) ||
                    path.Contains("BCat_Collar_C1", StringComparison.Ordinal) ||
                    path.Contains("Main CameraB", StringComparison.Ordinal) ||
                    path.Contains("MainCameraB", StringComparison.Ordinal));
        }

        private static void BindDirectorOutputsToAnimator(PlayableDirector director, Animator animator)
        {
            if (director == null || director.playableAsset == null || animator == null)
            {
                return;
            }

            foreach (PlayableBinding output in director.playableAsset.outputs)
            {
                if (output.sourceObject == null)
                {
                    continue;
                }

                bool isAnimationOutput = output.sourceObject is AnimationTrack ||
                                         output.outputTargetType == typeof(Animator) ||
                                         output.outputTargetType == typeof(GameObject);
                if (!isAnimationOutput)
                {
                    continue;
                }

                UnityEngine.Object currentBinding = director.GetGenericBinding(output.sourceObject);
                if (currentBinding != null)
                {
                    continue;
                }

                director.SetGenericBinding(output.sourceObject, animator);
            }
        }

        private static bool IsAnimationOutput(PlayableBinding output)
        {
            return output.sourceObject is AnimationTrack ||
                   output.outputTargetType == typeof(Animator) ||
                   output.outputTargetType == typeof(GameObject);
        }

        private static bool IsCameraOutput(PlayableBinding output)
        {
            string sourceName = output.sourceObject != null ? output.sourceObject.name : string.Empty;
            string text = $"{sourceName} {output.streamName}";
            return text.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.IndexOf("CameraB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   text.Contains("カメラ", StringComparison.Ordinal);
        }

        private static bool IsMoveStartCameraTrack(PlayableDirector director, PlayableBinding output)
        {
            string assetName = director != null && director.playableAsset != null ? director.playableAsset.name : string.Empty;
            if (!string.Equals(assetName, "OBOP_MoveStart", StringComparison.Ordinal))
            {
                return false;
            }

            string sourceName = output.sourceObject != null ? output.sourceObject.name : string.Empty;
            string streamName = output.streamName ?? string.Empty;
            return string.Equals(sourceName, "Animation Track (2)", StringComparison.Ordinal) ||
                   string.Equals(streamName, "Animation Track (2)", StringComparison.Ordinal);
        }

        private static Animator FindAnimatorNearDirector(PlayableDirector director)
        {
            if (director == null)
            {
                return null;
            }

            Transform current = director.transform;
            while (current != null)
            {
                if (current.TryGetComponent(out Animator parentAnimator))
                {
                    return parentAnimator;
                }

                current = current.parent;
            }

            return director.GetComponentInChildren<Animator>(true);
        }

        private static Animator FindAnimatorByObjectName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            Animator[] animators = Resources.FindObjectsOfTypeAll<Animator>();
            foreach (Animator animator in animators)
            {
                if (animator == null || animator.gameObject == null || !animator.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (string.Equals(animator.gameObject.name, objectName, StringComparison.Ordinal))
                {
                    return animator;
                }
            }

            GameObject[] gameObjects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (GameObject gameObject in gameObjects)
            {
                if (gameObject == null || !string.Equals(gameObject.name, objectName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (gameObject.TryGetComponent(out Animator animator))
                {
                    return animator;
                }

                Animator childAnimator = gameObject.GetComponentInChildren<Animator>(true);
                if (childAnimator != null)
                {
                    return childAnimator;
                }
            }

            return null;
        }

        private static Animator FindLikelyCatAnimator()
        {
            Animator[] animators = Resources.FindObjectsOfTypeAll<Animator>();
            Animator firstSceneAnimator = null;
            foreach (Animator animator in animators)
            {
                if (animator == null || animator.gameObject == null || !animator.gameObject.scene.IsValid())
                {
                    continue;
                }

                string path = GetPath(animator.transform);
                string controllerName = animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : string.Empty;
                if (string.Equals(controllerName, "CatSimple_AnimContr", StringComparison.Ordinal))
                {
                    return animator;
                }

                if (path.Contains("Cat_Collar_C1", StringComparison.Ordinal) ||
                    path.Contains("CatSimple", StringComparison.Ordinal) ||
                    path.Contains("Cat_Simple", StringComparison.Ordinal))
                {
                    return animator;
                }

                firstSceneAnimator ??= animator;
            }

            if (firstSceneAnimator != null)
            {
            }

            return firstSceneAnimator;
        }

        private void HoldInitialCatCallTimelineAtFinalPose()
        {
            if (initialCatCallTimelineDirector == null || initialCatCallTimelineDirector.playableAsset == null)
            {
                return;
            }

            Transform holdRoot = GetCatHoldRoot();

            initialCatCallTimelineDirector.time = ResolveTimelineFinalEvaluationTime(initialCatCallTimelineDirector);
            initialCatCallTimelineDirector.Evaluate();
            holdRoot = GetCatHoldRoot();

            Vector3 position = holdRoot != null ? holdRoot.position : Vector3.zero;
            Quaternion rotation = holdRoot != null ? holdRoot.rotation : Quaternion.identity;

            initialCatCallTimelineDirector.stopped -= HandleInitialCatCallTimelineStopped;
            initialCatCallTimelineDirector.Pause();
            initialCatCallTimelineDirector.stopped += HandleInitialCatCallTimelineStopped;
            initialCatCallTimelineDirector.extrapolationMode = DirectorWrapMode.Hold;

            if (holdRoot != null)
            {
                holdRoot.SetPositionAndRotation(position, rotation);
            }
        }

        private void ReleaseInitialCatCallTimelineControl()
        {
            if (initialCatCallTimelineDirector == null)
            {
                return;
            }

            Transform holdRoot = GetCatHoldRoot();
            Vector3 position = holdRoot != null ? holdRoot.position : Vector3.zero;
            Quaternion rotation = holdRoot != null ? holdRoot.rotation : Quaternion.identity;
            initialCatCallTimelineDirector.stopped -= HandleInitialCatCallTimelineStopped;
            initialCatCallTimelineDirector.Stop();
            initialCatCallTimelineDirector.time = 0d;
            initialCatCallTimelineDirector.extrapolationMode = DirectorWrapMode.None;
            initialCatCallTimelineDirector.enabled = false;
            initialCatCallTimelineDirector.stopped += HandleInitialCatCallTimelineStopped;

            if (holdRoot != null)
            {
                holdRoot.SetPositionAndRotation(position, rotation);
            }
        }

        private static double ResolveTimelineFinalEvaluationTime(PlayableDirector director)
        {
            if (director == null)
            {
                return 0d;
            }

            double duration = director.duration;
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0d)
            {
                return director.time;
            }

            return Math.Max(0d, duration - 0.001d);
        }

        private void PlayCatAnimatorState(string stateName, bool preserveTransform)
        {
            if (catAnimator == null || string.IsNullOrWhiteSpace(stateName))
            {
                return;
            }

            if (!CanPlayAnimator(catAnimator))
            {
                return;
            }

            Transform animatorTransform = catAnimator.transform;
            Transform holdRoot = GetCatHoldRoot();
            Vector3 position = animatorTransform.position;
            Quaternion rotation = animatorTransform.rotation;
            Vector3 holdRootPosition = holdRoot != null ? holdRoot.position : Vector3.zero;
            Quaternion holdRootRotation = holdRoot != null ? holdRoot.rotation : Quaternion.identity;
            Vector3 holdRootLocalPosition = holdRoot != null ? holdRoot.localPosition : Vector3.zero;
            Quaternion holdRootLocalRotation = holdRoot != null ? holdRoot.localRotation : Quaternion.identity;

            catAnimator.enabled = true;
            catAnimator.speed = 1f;
            catAnimator.Play(stateName, 0, 0f);
            catAnimator.Update(0f);

            if (preserveTransform)
            {
                animatorTransform.SetPositionAndRotation(position, rotation);
                if (holdRoot != null)
                {
                    holdRoot.SetPositionAndRotation(holdRootPosition, holdRootRotation);
                    holdRoot.localPosition = holdRootLocalPosition;
                    holdRoot.localRotation = holdRootLocalRotation;
                }
            }
        }

        private void PlayPostInitialCatCallState(bool preserveTransform)
        {
            keepPostInitialCatCallState = true;
            CrossFadeCatAnimatorState(postInitialCatCallStateName, postInitialCatCallBlendSeconds, preserveTransform);
        }

        private void CrossFadeCatAnimatorState(string stateName, float blendSeconds, bool preserveTransform)
        {
            if (catAnimator == null || string.IsNullOrWhiteSpace(stateName))
            {
                return;
            }

            if (!CanPlayAnimator(catAnimator))
            {
                return;
            }

            Transform animatorTransform = catAnimator.transform;
            Transform holdRoot = GetCatHoldRoot();
            Vector3 position = animatorTransform.position;
            Quaternion rotation = animatorTransform.rotation;
            Vector3 holdRootPosition = holdRoot != null ? holdRoot.position : Vector3.zero;
            Quaternion holdRootRotation = holdRoot != null ? holdRoot.rotation : Quaternion.identity;
            Vector3 holdRootLocalPosition = holdRoot != null ? holdRoot.localPosition : Vector3.zero;
            Quaternion holdRootLocalRotation = holdRoot != null ? holdRoot.localRotation : Quaternion.identity;

            catAnimator.enabled = true;
            catAnimator.speed = 1f;

            if (blendSeconds <= 0f)
            {
                catAnimator.Play(stateName, 0, 0f);
                catAnimator.Update(0f);
            }
            else
            {
                catAnimator.CrossFadeInFixedTime(stateName, blendSeconds, 0, 0f);
                catAnimator.Update(0f);
            }

            if (preserveTransform)
            {
                animatorTransform.SetPositionAndRotation(position, rotation);
                if (holdRoot != null)
                {
                    holdRoot.SetPositionAndRotation(holdRootPosition, holdRootRotation);
                    holdRoot.localPosition = holdRootLocalPosition;
                    holdRoot.localRotation = holdRootLocalRotation;
                }
            }
        }

        private void EnsurePostInitialCatCallState()
        {
            if (!keepPostInitialCatCallState || catAnimator == null || string.IsNullOrWhiteSpace(postInitialCatCallStateName) || !CanPlayAnimator(catAnimator))
            {
                return;
            }

            AnimatorStateInfo currentState = catAnimator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo nextState = catAnimator.GetNextAnimatorStateInfo(0);
            bool isCurrentState = currentState.IsName(postInitialCatCallStateName);
            bool isNextState = catAnimator.IsInTransition(0) && nextState.IsName(postInitialCatCallStateName);
            if (isCurrentState || isNextState)
            {
                return;
            }
            CrossFadeCatAnimatorState(postInitialCatCallStateName, postInitialCatCallBlendSeconds, true);
        }

        private void StartHoldingCatTransformAfterInitialCall(bool capturePose = true)
        {
            Transform holdRoot = GetCatHoldRoot();
            if (holdRoot == null)
            {
                return;
            }

            holdRoot = GetCatHoldRoot();
            heldCatWorldPosition = holdRoot.position;
            heldCatWorldRotation = holdRoot.rotation;
            heldCatPose = capturePose ? CaptureCatPose(holdRoot) : null;
            holdCatTransformAfterInitialCall = true;
            holdRoot.SetPositionAndRotation(heldCatWorldPosition, heldCatWorldRotation);
            ApplyHeldCatPose();
        }

        private Transform GetCatHoldRoot()
        {
            if (catHoldRoot != null)
            {
                return catHoldRoot;
            }

            catHoldRoot = ResolveCatHoldRoot(initialCatCallTimelineDirector, catAnimator);
            return catHoldRoot;
        }

        private static Transform ResolveCatHoldRoot(PlayableDirector director, Animator animator)
        {
            if (animator != null)
            {
                return animator.transform;
            }

            if (director != null)
            {
                return director.transform;
            }

            return null;
        }

        private static TransformSnapshot[] CaptureCatPose(Transform root)
        {
            if (root == null)
            {
                return Array.Empty<TransformSnapshot>();
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            List<TransformSnapshot> snapshots = new List<TransformSnapshot>(Math.Max(0, transforms.Length - 1));
            foreach (Transform current in transforms)
            {
                if (current == null || current == root)
                {
                    continue;
                }

                if (!ShouldCaptureCatPoseTransform(current, root))
                {
                    continue;
                }

                snapshots.Add(new TransformSnapshot(current));
            }

            return snapshots.ToArray();
        }

        private static bool ShouldCaptureCatPoseTransform(Transform transform, Transform root)
        {
            for (Transform current = transform; current != null && current != root; current = current.parent)
            {
                if (current.GetComponent<LookTargetManager>() != null || current.GetComponent<PlayerLookTarget>() != null)
                {
                    return false;
                }

                string name = current.name;
                if (string.Equals(name, "LocalLookTargetManager", StringComparison.Ordinal) ||
                    string.Equals(name, "LocalPlayerLookTarget", StringComparison.Ordinal) ||
                    string.Equals(name, "GeneratedNekomataLookAimTarget", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private void ApplyHeldCatPose()
        {
            if (heldCatPose == null)
            {
                return;
            }

            ApplyCatPoseSnapshot(heldCatPose);
        }

        private void ApplyLastAlternateCompletedPose(string phase)
        {
            if (lastAlternateCompletedPose == null || lastAlternateCompletedPose.Length == 0)
            {
                return;
            }

            ApplyCatPoseSnapshot(lastAlternateCompletedPose);
        }

        private void ApplyAlternateMoveStartCompletedPose(string phase)
        {
            if (alternateMoveStartCompletedPose == null || alternateMoveStartCompletedPose.Length == 0)
            {
                ApplyLastAlternateCompletedPose(phase);
                return;
            }

            ApplyCatPoseSnapshot(alternateMoveStartCompletedPose);
        }

        private static void ApplyCatPoseSnapshot(TransformSnapshot[] snapshots)
        {
            if (snapshots == null)
            {
                return;
            }

            foreach (TransformSnapshot snapshot in snapshots)
            {
                snapshot.Apply();
            }
        }

        private static bool CanPlayAnimator(Animator animator)
        {
            return animator != null && animator.gameObject.activeInHierarchy && animator.runtimeAnimatorController != null;
        }

        private static string GetPath(Transform transform)
        {
            if (transform == null)
            {
                return "null";
            }

            Stack<string> names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private readonly struct TransformSnapshot
        {
            private readonly Transform transform;
            private readonly Vector3 localPosition;
            private readonly Quaternion localRotation;
            private readonly Vector3 localScale;

            public TransformSnapshot(Transform transform)
            {
                this.transform = transform;
                localPosition = transform.localPosition;
                localRotation = transform.localRotation;
                localScale = transform.localScale;
            }

            public void Apply()
            {
                if (transform == null)
                {
                    return;
                }

                transform.localPosition = localPosition;
                transform.localRotation = localRotation;
                transform.localScale = localScale;
            }
        }

        private IEnumerator WaitForTyping()
        {
            while (chatUI != null && chatUI.IsTyping)
            {
                yield return null;
            }
        }

        private IEnumerator WaitForAdvance()
        {
            bool advanced = false;
            Action handler = () => advanced = true;
            chatUI.OnWaitInputCompleted += handler;
            while (!advanced)
            {
                yield return null;
            }

            chatUI.OnWaitInputCompleted -= handler;
        }

        private void SetOnlyPanelActive(Component activePanel)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TitlePanelDebug($"SetOnlyPanelActive requested={FormatPanelDebug(activePanel)} before intro={FormatPanelDebug(introPanel)} about={FormatPanelDebug(aboutPanel)} character={FormatPanelDebug(characterSetupPanel)} callCat={FormatPanelDebug(callCatPanel)}");
#endif
            if (introPanel != null) introPanel.gameObject.SetActive(activePanel == introPanel);
            if (aboutPanel != null) aboutPanel.gameObject.SetActive(activePanel == aboutPanel);
            if (worldviewPanel != null) worldviewPanel.gameObject.SetActive(activePanel == worldviewPanel);
            if (trainingDataPanel != null) trainingDataPanel.gameObject.SetActive(activePanel == trainingDataPanel);
            if (creditPanel != null) creditPanel.gameObject.SetActive(activePanel == creditPanel);
            if (characterSetupPanel != null) characterSetupPanel.gameObject.SetActive(activePanel == characterSetupPanel);
            if (callCatPanel != null) callCatPanel.gameObject.SetActive(activePanel == callCatPanel);
#if UNITY_WEBGL && !UNITY_EDITOR
            TitlePanelDebug($"SetOnlyPanelActive after intro={FormatPanelDebug(introPanel)} about={FormatPanelDebug(aboutPanel)} character={FormatPanelDebug(characterSetupPanel)} callCat={FormatPanelDebug(callCatPanel)}");
#endif
        }

        private void HideAllPanels()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TitlePanelDebug($"HideAllPanels before intro={FormatPanelDebug(introPanel)} about={FormatPanelDebug(aboutPanel)} character={FormatPanelDebug(characterSetupPanel)} callCat={FormatPanelDebug(callCatPanel)}");
#endif
            if (introPanel != null) introPanel.gameObject.SetActive(false);
            if (aboutPanel != null) aboutPanel.gameObject.SetActive(false);
            if (worldviewPanel != null) worldviewPanel.gameObject.SetActive(false);
            if (trainingDataPanel != null) trainingDataPanel.gameObject.SetActive(false);
            if (creditPanel != null) creditPanel.gameObject.SetActive(false);
            if (characterSetupPanel != null) characterSetupPanel.gameObject.SetActive(false);
            if (callCatPanel != null) callCatPanel.gameObject.SetActive(false);
#if UNITY_WEBGL && !UNITY_EDITOR
            TitlePanelDebug($"HideAllPanels after intro={FormatPanelDebug(introPanel)} about={FormatPanelDebug(aboutPanel)} character={FormatPanelDebug(characterSetupPanel)} callCat={FormatPanelDebug(callCatPanel)}");
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static string FormatPanelDebug(Component panel)
        {
            if (panel == null)
            {
                return "<null>";
            }

            GameObject target = panel.gameObject;
            CanvasGroup canvasGroup = target.GetComponent<CanvasGroup>();
            RectTransform rect = target.transform as RectTransform;
            return $"{panel.GetType().Name}:{target.name} id={target.GetInstanceID()} activeSelf={target.activeSelf} activeInHierarchy={target.activeInHierarchy} scene={target.scene.name} path={GetTransformPath(target.transform)} rect={FormatRectDebug(rect)} canvasGroup={FormatCanvasGroupDebug(canvasGroup)}";
        }

        private static string FormatRectDebug(RectTransform rect)
        {
            if (rect == null)
            {
                return "<null>";
            }

            Rect r = rect.rect;
            return $"pos=({rect.anchoredPosition.x:0.##},{rect.anchoredPosition.y:0.##}) size=({r.width:0.##},{r.height:0.##}) scale=({rect.lossyScale.x:0.###},{rect.lossyScale.y:0.###},{rect.lossyScale.z:0.###})";
        }

        private static string FormatCanvasGroupDebug(CanvasGroup group)
        {
            if (group == null)
            {
                return "<none>";
            }

            return $"alpha={group.alpha:0.###},interactable={group.interactable},blocks={group.blocksRaycasts},ignoreParent={group.ignoreParentGroups}";
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            Stack<string> names = new Stack<string>();
            Transform current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }
#endif

        private string Resolve(string key)
        {
            return BasicSystemDialogueCatalog.Format(key, CreateRuntimePlaceholders(true), string.Empty);
        }

        private static void BindButton(Button button, Action action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action?.Invoke());
        }

        private IReadOnlyDictionary<string, string> CreateRuntimePlaceholders(bool highlightRenameWords = false)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PLAYER_NAME"] = FormatRuntimePlaceholderValue("PLAYER_NAME", playerName, highlightRenameWords),
                ["PlayerName"] = FormatRuntimePlaceholderValue("PlayerName", playerName, highlightRenameWords),
                ["CAT_NAME"] = FormatRuntimePlaceholderValue("CAT_NAME", catName, highlightRenameWords),
                ["CatName"] = FormatRuntimePlaceholderValue("CatName", catName, highlightRenameWords),
                ["PLAYER_CALLING"] = FormatRuntimePlaceholderValue("PLAYER_CALLING", playerCalling, highlightRenameWords),
                ["PlayerCalling"] = FormatRuntimePlaceholderValue("PlayerCalling", playerCalling, highlightRenameWords),
                ["CAT_PRONOUN"] = FormatRuntimePlaceholderValue(
                    "CAT_PRONOUN",
                    catPronoun,
                    highlightRenameWords)
            };
        }

        private static string FormatRuntimePlaceholderValue(string key, string value, bool highlightRenameWords)
        {
            string safeValue = SanitizeSetupValue(value);
            if (!highlightRenameWords || string.IsNullOrEmpty(safeValue) || !IsRenameWordPlaceholder(key))
            {
                return safeValue;
            }

            return $"<color={GetRenameWordColor(key)}>{EscapeRichTextValue(safeValue)}</color>";
        }

        private static string GetRenameWordColor(string key)
        {
            if (string.Equals(key, "CAT_NAME", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "CAT_PRONOUN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "CatName", StringComparison.OrdinalIgnoreCase))
            {
                return CatRenameWordColor;
            }

            if (string.Equals(key, "PLAYER_NAME", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "PLAYER_CALLING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "PlayerName", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "PlayerCalling", StringComparison.OrdinalIgnoreCase))
            {
                return PlayerRenameWordColor;
            }

            return PendingRenameWordColor;
        }

        private static bool IsRenameWordPlaceholder(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string normalized = key.Trim().Trim('{', '}').Replace("_", string.Empty).ToUpperInvariant();
            return normalized == "CATNAME" ||
                   normalized == "PLAYERCALLING" ||
                   normalized == "CATPRONOUN" ||
                   normalized == "PENDINGVALUE";
        }

        private static string EscapeRichTextValue(string value)
        {
            return string.IsNullOrEmpty(value)
                ? value
                : value.Replace("<", "‹").Replace(">", "›");
        }
    }
}
