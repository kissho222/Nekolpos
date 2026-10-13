using System.Collections.Generic;
using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// タブレット画面への接触と近接中の自動消灯を管理する。
    /// 点灯状態だけを所有し、カメラを含むタブレット操作状態は
    /// <see cref="NekolposGameBootstrap"/> に委ねる。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TabletScreenWakeController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private NekolposGameBootstrap gameBootstrap;
        [SerializeField] private BoxCollider screenTrigger;

        [Header("Sleep")]
        [Tooltip("Playerまたは猫の足が画面Triggerから離れた後、画面を消灯するまでの秒数です。タブレット操作中は使用しません。")]
        [SerializeField, Min(0f)] private float sleepDelaySeconds = 8f;

        private readonly HashSet<Collider> presentSourceColliders = new HashSet<Collider>();
        private float lastPresenceTime;

        public bool IsAwake => gameBootstrap != null && gameBootstrap.IsTabletDisplayPowered;
        public bool HasWakeSourceNearby => presentSourceColliders.Count > 0;
        public float SleepDelaySeconds => sleepDelaySeconds;

        private void Awake()
        {
            screenTrigger ??= GetComponent<BoxCollider>();
            if (screenTrigger != null)
            {
                screenTrigger.isTrigger = true;
            }

            Rigidbody body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        private void Update()
        {
            if (!IsAwake || HasWakeSourceNearby || gameBootstrap.IsTabletCameraViewActive)
            {
                return;
            }

            if (Time.unscaledTime - lastPresenceTime >= sleepDelaySeconds)
            {
                gameBootstrap.SleepTabletDisplay();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsWakeSource(other) || !presentSourceColliders.Add(other))
            {
                return;
            }

            WakeTablet();
        }

        private void OnTriggerExit(Collider other)
        {
            if (presentSourceColliders.Remove(other) && !HasWakeSourceNearby)
            {
                lastPresenceTime = Time.unscaledTime;
            }
        }

        private void OnDisable()
        {
            presentSourceColliders.Clear();
        }

        /// <summary>接触開始や外部イベントから共通の点灯処理を呼び出す。</summary>
        public void WakeTablet()
        {
            lastPresenceTime = Time.unscaledTime;
            gameBootstrap?.WakeTabletDisplay();
        }

        /// <summary>散歩中のPlayerInteractorから、現在の画面を維持したまま操作を開始する。</summary>
        public bool TryBeginTabletInteraction(PlayerInteractor interactor)
        {
            if (!IsAwake || gameBootstrap == null || interactor == null || interactor.WalkModeController == null)
            {
                return false;
            }

            return gameBootstrap.EnterTabletCameraViewFromWalk(interactor.WalkModeController);
        }

        public bool CanBeginTabletInteraction(PlayerInteractor interactor)
        {
            return IsAwake &&
                   gameBootstrap != null &&
                   !gameBootstrap.IsTabletCameraTransitioning &&
                   !gameBootstrap.IsTabletCameraViewActive &&
                   interactor != null &&
                   interactor.WalkModeController != null &&
                   interactor.WalkModeController.IsWalkMode &&
                   !interactor.WalkModeController.IsTabletInteractionActive;
        }

        public void Configure(NekolposGameBootstrap bootstrap, BoxCollider trigger, float sleepDelay)
        {
            gameBootstrap = bootstrap;
            screenTrigger = trigger;
            sleepDelaySeconds = Mathf.Max(0f, sleepDelay);
            if (screenTrigger != null)
            {
                screenTrigger.isTrigger = true;
            }
        }

        private static bool IsWakeSource(Collider collider)
        {
            return collider != null && collider.GetComponentInParent<TabletWakeSource>() != null;
        }
    }

    /// <summary>
    /// タブレット画面を点灯できるColliderを明示するマーカー。
    /// 猫は前後の足にだけ付けるため、胴体・頭部・尻尾では誤点灯しない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TabletWakeSource : MonoBehaviour
    {
        public enum SourceKind
        {
            Player,
            CatPaw
        }

        [SerializeField] private SourceKind sourceKind;

        public SourceKind Kind => sourceKind;

        public void Configure(SourceKind kind)
        {
            sourceKind = kind;
        }
    }

    /// <summary>点灯中だけ散歩用の既存PlayerInteractorへタブレット操作を公開する。</summary>
    [DisallowMultipleComponent]
    public sealed class TabletWorldInteractable : global::PlayerInteractable
    {
        [SerializeField] private TabletScreenWakeController wakeController;

        public override string PromptText => "E：操作する";
        // 操作開始後はPlayerInteractor側が散歩入力を停止するため、ここで状態を保持しない。
        public override bool IsInteracting => false;

        public void Configure(TabletScreenWakeController controller)
        {
            wakeController = controller;
        }

        public override bool CanInteract(global::PlayerInteractor interactor)
        {
            return base.CanInteract(interactor) && wakeController != null && wakeController.CanBeginTabletInteraction(interactor);
        }

        public override bool TryInteract(global::PlayerInteractor interactor)
        {
            return CanInteract(interactor) && wakeController.TryBeginTabletInteraction(interactor);
        }
    }
}
