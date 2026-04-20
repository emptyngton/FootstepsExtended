using System;
using UnityEngine;

namespace customevent
{
    public class EventType
    {   // using enums crashes vam
        public const byte NONE = 0;
        public const byte ENTERED = 1;
        public const byte HIT = 2;
        public const byte EXITED = 3;
        public const byte STAY = 4;
    }

    public class TriggerEventArgs : EventArgs
    {
        public Collision collision { get; set; }
        public Collider collider { get; set; }
        public byte evtType { get; set; }
        public bool insideTrigger { get; set; }
    }

    public class TriggerCollide : MonoBehaviour
    {
        TriggerEventArgs lastEvent;

        bool inside = false;

        public static float minTimeBetweenCollisions = 0.25f;
        float lastCollisionTime = 0;

        public event EventHandler<TriggerEventArgs> OnCollide;

        void Awake()
        {
            lastEvent = new TriggerEventArgs
            {
                evtType = EventType.NONE,
                collider = null,
                collision = null,
                insideTrigger = false
            };
        }

        private void OnTriggerEnter(Collider other)
        {
            //other.isTrigger = false;
            inside = true;
            //DoCollideEvent(EventType.ENTERED, other, null);
        }

        private void OnTriggerExit(Collider other)
        {
            inside = false;
            lastEvent.evtType = EventType.EXITED;
        }

        protected virtual void OnCollideEvent(TriggerEventArgs e)
        {
           // SuperController.LogMessage("sending event: " + e.collider.name + ", " + e.insideTrigger);
            EventHandler<TriggerEventArgs> handler = OnCollide;
            handler?.Invoke(this, e);
        }

        protected virtual void OnCollisionEnter(Collision collision)
        {
            DoCollideEvent(EventType.HIT, collision.collider, collision);
        }

        protected virtual void OnCollisionExit(Collision collision)
        {
            lastEvent.evtType = EventType.EXITED;
        }

        private void DoCollideEvent(byte evtType, Collider col, Collision c)
        {
            if (Time.timeSinceLevelLoad - lastCollisionTime > minTimeBetweenCollisions)
            {
                if (evtType == lastEvent.evtType || c == null)
                {
                    return;
                }
                else
                {
                    //SuperController.LogMessage("collide: " + col.name + "time: " + Time.timeSinceLevelLoad);

                    lastCollisionTime = Time.timeSinceLevelLoad;

                    TriggerEventArgs tempEvent = new TriggerEventArgs
                    {
                        evtType = evtType,
                        collider = col,
                        collision = c,
                        insideTrigger = inside
                    };

                    OnCollideEvent(tempEvent);
                    lastEvent = tempEvent;
                }
            }
        }
    }


}
