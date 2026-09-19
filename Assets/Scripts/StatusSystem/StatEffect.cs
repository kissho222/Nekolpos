using System;

namespace Nekolpos.StatusSystem
{
    [Serializable]
    public struct StatEffect
    {
        public StatusType statusType;
        public int value;

        public StatEffect(StatusType statusType, int value)
        {
            this.statusType = statusType;
            this.value = value;
        }
    }
}
