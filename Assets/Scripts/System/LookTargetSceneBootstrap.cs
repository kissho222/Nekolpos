using UnityEngine;

namespace Nekolpos.System
{
    public static class LookTargetSceneBootstrap
    {
        private const string ManagerObjectName = "LookTargetControls";
        private const string PlayerTargetObjectName = "PlayerLookTarget_AdjustHere";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureLookTargetObjects()
        {
            LookTargetManager manager = Object.FindFirstObjectByType<LookTargetManager>();
            if (manager == null)
            {
                GameObject managerObject = new GameObject(ManagerObjectName);
                manager = managerObject.AddComponent<LookTargetManager>();
            }

            PlayerLookTarget playerLookTarget = Object.FindFirstObjectByType<PlayerLookTarget>();
            if (playerLookTarget == null)
            {
                GameObject playerTargetObject = new GameObject(PlayerTargetObjectName);
                playerTargetObject.transform.SetParent(manager.transform, true);
                playerLookTarget = playerTargetObject.AddComponent<PlayerLookTarget>();
            }
            else if (playerLookTarget.gameObject.name == PlayerTargetObjectName && playerLookTarget.transform.parent == null)
            {
                playerLookTarget.transform.SetParent(manager.transform, true);
            }

            manager.SetDefaultTarget(playerLookTarget.transform);
        }
    }
}
