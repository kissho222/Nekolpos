using UnityEngine;

namespace Nekolpos.System
{
    [DisallowMultipleComponent]
    public sealed class NekomataLookRigSceneOverride : MonoBehaviour
    {
        [Tooltip("オンにすると、このシーン内の NekomataLookRigController による頭・目の視線追従を一時停止します。")]
        [SerializeField] private bool disableLookRig;

        private static int activeDisableOverrideCount;
        private bool isContributingDisableOverride;

        public bool DisableLookRig
        {
            get => disableLookRig;
            set
            {
                disableLookRig = value;
                SyncDisableOverrideContribution();
            }
        }

        public static bool IsLookRigDisabledInScene()
        {
            return activeDisableOverrideCount > 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            activeDisableOverrideCount = 0;
        }

        private void OnEnable()
        {
            SyncDisableOverrideContribution();
        }

        private void OnDisable()
        {
            RemoveDisableOverrideContribution();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled)
            {
                SyncDisableOverrideContribution();
            }
            else
            {
                RemoveDisableOverrideContribution();
            }
        }

        private void SyncDisableOverrideContribution()
        {
            if (disableLookRig)
            {
                AddDisableOverrideContribution();
            }
            else
            {
                RemoveDisableOverrideContribution();
            }
        }

        private void AddDisableOverrideContribution()
        {
            if (isContributingDisableOverride)
            {
                return;
            }

            activeDisableOverrideCount++;
            isContributingDisableOverride = true;
        }

        private void RemoveDisableOverrideContribution()
        {
            if (!isContributingDisableOverride)
            {
                return;
            }

            activeDisableOverrideCount = Mathf.Max(0, activeDisableOverrideCount - 1);
            isContributingDisableOverride = false;
        }
    }
}
