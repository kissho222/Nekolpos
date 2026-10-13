using System;
using UnityEngine;

/// <summary>散歩中のAreaまたは任意Triggerから専用コメントを一度だけ/クールダウン付きで起動する。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class WalkDialogueTrigger : MonoBehaviour
{
    [SerializeField] private string triggerId;
    [SerializeField] private bool playOnce;
    [SerializeField, Min(0f)] private float cooldownSeconds = 10f;
    private bool played;
    private float nextAllowedTime;

    private void Reset() => GetComponent<Collider>().isTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        if (played && playOnce || Time.unscaledTime < nextAllowedTime) return;
        WalkDialogueController controller = other.GetComponentInParent<WalkDialogueController>();
        if (controller != null && controller.PlayInteraction(triggerId))
        {
            played = true;
            nextAllowedTime = Time.unscaledTime + cooldownSeconds;
        }
    }
}
