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
        // atom (one-shot AddComponent if missing) and subscribe to its OnCollide
        // event. The try/catch is the cheap way to do GetOrAdd — GetComponent
        // returns null on absence, and calling .OnCollide on null throws.
        public void RefreshColliders()
        {
            containingAtom.GetComponentsInChildren<Rigidbody>().ToList().ForEach(f =>
            {
                try
                {
                    f.gameObject.GetComponent<TriggerCollide>().OnCollide += ObserveTrigger;
                }
                catch
                {
                    f.gameObject.AddComponent<TriggerCollide>().OnCollide += ObserveTrigger;
                }
            });
        }

        // Per-collision handler. Walks the colliding hierarchy to find the owning
        // Atom (so we can filter by atom name/type), then forwards to RaeClickTouch
        // if the impact's relative-velocity magnitude is within the configured
        // [Impact Min, Impact Max] window. `myClock` is the post-step cooldown gate.
        void ObserveTrigger(object sender, TriggerEventArgs e)
        {
            if (myClock > 0f) return;
            if (e.evtType != customevent.EventType.HIT) return;
            if (e.insideTrigger) return;

            float level = e.collision.relativeVelocity.magnitude / 3.0f;
            if (level <= treshold1.val || level >= treshold2.val) return;

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

            string xname = found.Length > 0 ? found : parent.name;
            string xcollider = e.collider.name;
            if (e.collision.contacts.Count() > 0)
                xcollider = e.collision.contacts[0].otherCollider.attachedRigidbody.name;

            RaeClickTouch(sender.ToString(), xname, parent.type, xcollider);
        }

        // Apply user-configured filters, then fire the appropriate side trigger
        // and play a sound. The sender string carries TriggerCollide's component
        // signature suffix; strip it so the rigidbody name compares cleanly.
        void RaeClickTouch(string sender, string name, string type, string collider)
        {
            if (IsDisabled()) return;
            sender = sender.Replace(" (customevent.TriggerCollide)", "");

            // Don't trigger on self-collisions (e.g. foot-to-leg).
            if (name == containingAtom.name) return;

            if (variableThisElements.Length      > 0 && Array.IndexOf(variableThisElements, sender)        == -1) return;
            if (variableColliderGroups.Length    > 0 && Array.IndexOf(variableColliderGroups, type)        == -1) return;
            if (variableColliderIds.Length       > 0 && Array.IndexOf(variableColliderIds, name)           == -1) return;
            if (variableColliderElements.Length  > 0 && Array.IndexOf(variableColliderElements, collider)  == -1) return;

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
