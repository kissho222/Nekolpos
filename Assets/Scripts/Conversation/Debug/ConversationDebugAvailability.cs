using UnityEngine;

namespace Backgammon.Conversation
{
    public static class ConversationDebugAvailability
    {
        public static bool IsEnabled
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }
    }
}
