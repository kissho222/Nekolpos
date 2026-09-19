using UnityEngine;

namespace Backgammon.Conversation
{
    public static class ConversationDebugAvailability
    {
        public static bool IsEnabled
        {
            get
            {
#if UNITY_EDITOR
                return true;
#else
                return Debug.isDebugBuild;
#endif
            }
        }
    }
}
