using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// Shared look target hub. Character-side code should read CurrentTarget only.
    /// </summary>
    public class LookTargetManager : MonoBehaviour
    {
        [SerializeField] private Transform defaultTarget;
        [SerializeField] private Transform currentTarget;

        public Transform CurrentTarget => currentTarget != null ? currentTarget : defaultTarget;

        public bool HasExplicitTarget => currentTarget != null;

        public void SetDefaultTarget(Transform target)
        {
            defaultTarget = target;
        }

        public void SetTarget(Transform target)
        {
            currentTarget = target;
        }

        public void ClearTarget()
        {
            currentTarget = null;
        }
    }
}
