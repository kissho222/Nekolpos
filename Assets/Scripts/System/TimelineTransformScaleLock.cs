using UnityEngine;

namespace Nekolpos.System
{
    /// <summary>
    /// Keeps a transform's model-unit conversion scale after Timeline/Animator evaluation.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class TimelineTransformScaleLock : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 localScale = Vector3.one;

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void LateUpdate()
        {
            Apply();
        }

        private void Apply()
        {
            if (target != null && target.localScale != localScale)
            {
                target.localScale = localScale;
            }
        }
    }
}
