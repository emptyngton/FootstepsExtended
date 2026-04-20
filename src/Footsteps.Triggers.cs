using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using customevent;

namespace SPQR
{
    public partial class Footsteps : MVRScript
    {
        // Attach a TriggerCollide MonoBehaviour to every rigidbody in the Person
        // atom (idempotent: GetOrAddComponent), then re-subscribe ObserveTrigger
        // to its OnCollide event. The unsubscribe-then-subscribe pair keeps this
        // safe to call multiple times — the EnsureSetupReady retry loop relies
        // on that, as does OnEnable.
        public void RefreshColliders()
        {
            int subscribed = 0;
            foreach (var f in containingAtom.GetComponentsInChildren<Rigidbody>())
            {
                var tc = f.gameObject.GetComponent<TriggerCollide>();
                if (tc == null) tc = f.gameObject.AddComponent<TriggerCollide>();
                tc.OnCollide -= ObserveTrigger;
                tc.OnCollide += ObserveTrigger;
                subscribed++;
            }
            Debug("RefreshColliders: subscribed to " + subscribed + " rigidbodies");
        }

        // Per-collision handler. The Person atom subscribes ObserveTrigger to
        // EVERY rigidbody (~30 of them), so this fires constantly from limbs
        // bumping each other. Keep the cheap filters at the top so most calls
        // bail before we do any parent-walking or logging.
        void ObserveTrigger(object sender, TriggerEventArgs e)
        {
            // Orphan-instance guard. If a previous plugin instance was destroyed
            // but its delegate is still on a TriggerCollide.OnCollide invocation
            // list (cleanup-on-destroy didn't run cleanly — typically because
            // containingAtom was already null at that point), this fires from
            // beyond the grave. Bail silently so it doesn't pollute the log.
            // Unity's overloaded == correctly reports true for destroyed objects.
            if (containingAtom == null) return;

            if (e.evtType != customevent.EventType.HIT) return;
            if (e.insideTrigger) return;

            // Cheapest filter first: only foot rigidbodies are interesting. This
            // drops the firehose of thigh/shin/arm self-collisions silently,
            // without spamming the debug log. Use the TriggerCollide's
            // GameObject name directly to avoid allocating a Replace() string
            // for every collision.
            TriggerCollide tc = sender as TriggerCollide;
            string senderName = tc != null ? tc.gameObject.name : sender.ToString().Replace(" (customevent.TriggerCollide)", "");
            if (variableThisElements.Length > 0 && Array.IndexOf(variableThisElements, senderName) == -1)
                return;

            // Per-foot cooldown gate. Right and left have independent clocks so
            // a simultaneous double-foot landing (jump, two-footed stomp) fires
            // both sounds instead of just whichever one Unity dispatched first.
            bool isRight = senderName == "rFoot" || senderName == "rToe" || senderName == "rBigToe";
            if (isRight ? rightClock > 0f : leftClock > 0f) return;

            // Velocity gate. Log near-misses (level > 0 but below min) so users
            // can see "I almost stepped, lower Impact Min" — but skip the
            // zero-energy noise from limbs in resting contact.
            float level = e.collision.relativeVelocity.magnitude / 3.0f;
            if (level <= treshold1.val || level >= treshold2.val)
            {
                if (level > 0.01f)
                    Debug("ObserveTrigger: bailed (level " + level.ToString("F2") + " outside [" + treshold1.val.ToString("F2") + ".." + treshold2.val.ToString("F2") + "])");
                return;
            }

            // Expensive: walk parents to find the owning Atom of the OTHER side.
            Atom parent = null;
            foreach (var component in e.collider.gameObject.GetComponentsInParent<Component>())
            {
                var atom = component as Atom;
                if (atom != null) { parent = atom; break; }
            }

            // Identify the *other* atom by matching its first rigidbody to the
            // attachedRigidbody of the collision. Fallback for cases where the
            // collider's own GameObject doesn't carry the Atom reference.
            string found = "";
            var RB = e.collider.attachedRigidbody;
            foreach (var xatom in SuperController.singleton.GetAtoms())
            {
                if (RB == xatom.rigidbodies.First())
                {
                    found = xatom.name;
                    break;
                }
            }

            string xname = found.Length > 0 ? found : (parent != null ? parent.name : "?");
            string xtype = parent != null ? parent.type : "?";
            string xcollider = e.collider.name;
            if (e.collision.contacts.Count() > 0)
                xcollider = e.collision.contacts[0].otherCollider.attachedRigidbody.name;

            // Drop self-collisions (foot brushed our own knee/thigh) silently.
            if (xname == containingAtom.name) return;

            Debug("ObserveTrigger: hit sender='" + senderName + "' other='" + xname + "' type='" + xtype + "' collider='" + xcollider + "' level=" + level.ToString("F2"));
            RaeClickTouch(senderName, xname, xtype, xcollider, isRight);
        }

        // Apply the OTHER-side filters (atom type / atom name / collider name),
        // route to the appropriate side, fire the EventTriggers, play sound.
        // Sender filter, self-collision, and per-foot cooldown are already
        // handled by ObserveTrigger. `isRight` is the side decision computed
        // there, so we don't reclassify here.
        void RaeClickTouch(string sender, string name, string type, string collider, bool isRight)
        {
            if (IsDisabled()) { Debug("RaeClickTouch: bailed (plugin disabled)"); return; }

            if (variableColliderGroups.Length > 0 && Array.IndexOf(variableColliderGroups, type) == -1)
            { Debug("RaeClickTouch: bailed (other-type '" + type + "' not in Collider Group filter)"); return; }
            if (variableColliderIds.Length > 0 && Array.IndexOf(variableColliderIds, name) == -1)
            { Debug("RaeClickTouch: bailed (other-name '" + name + "' not in Collider Atom filter)"); return; }
            if (variableColliderElements.Length > 0 && Array.IndexOf(variableColliderElements, collider) == -1)
            { Debug("RaeClickTouch: bailed (other-collider '" + collider + "' not in Collider Element filter)"); return; }

            if (isRight)
            {
                collisionsPreview.val = "Right Foot";
                FootstepsRight();
                rightClock = jfloat2.val;
            }
            else
            {
                collisionsPreview.val = "Left Foot";
                FootstepsLeft();
                leftClock = jfloat2.val;
            }

            dtext4.backgroundColor = new Color(0.33f, 1f, 0.33f, 0.7f);
            StartCoroutine(CoroutineFlashTrigger());
        }

        void Trigger()      { myEventTrigger.Trigger(); }
        void TriggerLeft()  { myEventTriggerLeft.Trigger(); }
        void TriggerRight() { myEventTriggerRight.Trigger(); }

        void FootstepsLeft()
        {
            Trigger();
            TriggerLeft();
            if (soundChooser.val == "None") return;
            PlayFootSound(leftFootAudio, GetRandomSound());
        }

        void FootstepsRight()
        {
            Trigger();
            TriggerRight();
            if (soundChooser.val == "None") return;
            PlayFootSound(rightFootAudio, GetRandomSound());
        }

        // Flash the indicator green for 0.33s, then revert to red.
        IEnumerator CoroutineFlashTrigger()
        {
            yield return new WaitForSeconds(0.33f);
            collisionsPreview.val = "";
            dtext4.backgroundColor = new Color(1f, 0.33f, 0.33f, 0.7f);
        }
    }
}
