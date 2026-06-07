using UnityEngine;

namespace NGO.Gameplay.Base
{
    public abstract class TimerBase : MonoBehaviour
    {
        public float InitialTime = 180f;
        public virtual float RemainingTime { get; protected set; }
        public bool IsFinished => RemainingTime <= 0;

        protected virtual void Update()
        {
            if (RemainingTime > 0)
                RemainingTime -= Time.deltaTime;
        }
    }
}
