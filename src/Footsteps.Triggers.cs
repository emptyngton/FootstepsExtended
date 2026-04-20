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

        // Per-collision handler. Walks the colliding hierarchy to find the owning
        // Atom (so we can filter by atom name/type), then forwards to RaeClickTouch
        // if the impact's relative-velocity magnitude is within the configured
        // [Impact Min, Impact Max] window. `myClock` is the post-step cooldown gate.
        void ObserveTrigger(object sender, TriggerEventArgs e)
        {
            if (myClock > 0f) { Debug("ObserveTrigger: bailed (cooldown active, " + myClock.ToString("F2") + "s left)"); return; }
            if (e.evtType != customevent.EventType.HIT) return;
            if (e.insideTrigger) return;

            float level = e.collision.relativeVelocity.magnitude / 3.0f;
            if (level <= treshold1.val || level >= treshold2.val)
            {
                Debug("ObserveTrigger: bailed (level " + level.ToString("F2") + " outside [" + treshold1.val.ToString("F2") + ".." + treshold2.val.ToString("F2") + "])");
                return;
            }

            // Find the owning Atom by walking parents from the colliding GameObject.
            Atom parent = null;
            foreach (var component in e.collider.gameObject.GetComponentsInParent<Component>())
            {
                var atom = component as Atom;
                if (atom != null) { parent = atom; break; }
            }

            // Identify the *other* atom by matching its first rigidbody to the
            // attachedRigidbody of the collision. This is a workaround for the
            // collider's own GameObject not always carrying the Atom reference.
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

            Debug("ObserveTrigger: hit sender='" + sender + "' other='" + xname + "' type='" + xtype + "' collider='" + xcollider + "' level=" + level.ToString("F2"));
            RaeClickTouch(sender.ToString(), xname, xtype, xcollider);
        }

        // Apply user-configured filters, then fire the appropriate side trigger
        // and play a sound. The sender string carries TriggerCollide's component
        // signature suffix; strip it so the rigidbody name compares cleanly.
        void RaeClickTouch(string sender, string name, string type, string collider)
        {
            if (IsDisabled()) { Debug("RaeClickTouch: bailed (plugin disabled)"); return; }
            sender = sender.Replace(" (customevent.TriggerCollide)", "");

            // Don't trigger on self-collisions (e.g. foot-to-leg).
            if (name == containingAtom.name) { Debug("RaeClickTouch: bailed (self-collision)"); return; }

            if (variableThisElements.Length > 0 && Array.IndexOf(variableThisElements, sender) == -1)
            { Debug("RaeClickTouch: bailed (sender '" + sender + "' not in Element filter)"); return; }
            if (variableColliderGroups.Length > 0 && Array.IndexOf(variableColliderGroups, type) == -1)
            { Debug("RaeClickTouch: bailed (other-type '" + type + "' not in Collider Group filter)"); return; }
            if (variableColliderIds.Length > 0 && Array.IndexOf(variableColliderIds, name) == -1)
            { Debug("RaeClickTouch: bailed (other-name '" + name + "' not in Collider Atom filter)"); return; }
            if (variableColliderElements.Length > 0 && Array.IndexOf(variableColliderElements, collider) == -1)
            { Debug("RaeClickTouch: bailed (other-collider '" + collider + "' not in Collider Element filter)"); return; }

            if (sender == "rToe" || sender == "rBigToe" || sender == "rFoot")
            {
                collisionsPreview.val = "Right Foot";
                FootstepsRight();
            }
            else
            {
                collisionsPreview.val = "Left Foot";
                FootstepsLeft();
            }

            dtext4.backgroundColor = new Color(0.33f, 1f, 0.33f, 0.7f);
            StartCoroutine(CoroutineFlashTrigger());
            myClock = jfloat2.val;
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
