using System;
using System.Collections;
using Random2 = UnityEngine.Random;
using MacGruber;

namespace SPQR
{
    public partial class Footsteps : MVRScript
    {
        const string SoundsRoot = "Custom/Sounds/FootstepsSFX/";

        string GetIdFootstepsLeft()  { return containingAtom.uid + ".footsteps.left"; }
        string GetIdFootstepsRight() { return containingAtom.uid + ".footsteps.right"; }

        // Spawn the two child AudioSource atoms ("<person>.footsteps.left/right")
        // and parent-link them to the foot rigidbodies. Idempotent — only spawns
        // atoms that don't already exist, so multiple Init/UpdateSecond passes
        // won't duplicate them.
        void FootstepsInit()
        {
            try
            {
                var leftId = GetIdFootstepsLeft();
                var rightId = GetIdFootstepsRight();
                if (SuperController.singleton.GetAtomByUid(leftId) == null)
                    StartCoroutine(CoroutineCreateAtomLeft(leftId));
                if (SuperController.singleton.GetAtomByUid(rightId) == null)
                    StartCoroutine(CoroutineCreateAtomRight(rightId));
            }
            catch
            {
            }
            initiated = true;
        }

        IEnumerator CoroutineCreateAtomLeft(string atomid)
        {
            yield return SuperController.singleton.AddAtomByType("AudioSource", atomid);
            FreeControllerV3 lController = containingAtom.GetStorableByID("lFootControl") as FreeControllerV3;
            FootstepsAlign(atomid, lController, "lFoot");
        }

        IEnumerator CoroutineCreateAtomRight(string atomid)
        {
            yield return SuperController.singleton.AddAtomByType("AudioSource", atomid);
            FreeControllerV3 rController = containingAtom.GetStorableByID("rFootControl") as FreeControllerV3;
            FootstepsAlign(atomid, rController, "rFoot");
        }

        void FootstepsAlign(string atomid, FreeControllerV3 footController, string rb)
        {
            Atom RAE = SuperController.singleton.GetAtomByUid(atomid);
            RAE.ClearParentAtom();
            RAE.SelectAtomParent(containingAtom);

            FreeControllerV3 raeController = RAE.GetStorableByID("control") as FreeControllerV3;

            // Put the AudioSource exactly on the foot, then switch to ParentLink
            // so it follows the foot rigidbody from there on.
            RAE.mainController.currentPositionState = FreeControllerV3.PositionState.On;
            RAE.mainController.currentRotationState = FreeControllerV3.RotationState.On;
            raeController.transform.position = footController.transform.position;
            raeController.transform.rotation = footController.transform.rotation;
            RAE.mainController.currentPositionState = FreeControllerV3.PositionState.ParentLink;
            RAE.mainController.currentRotationState = FreeControllerV3.RotationState.ParentLink;
            raeController.SetLinkToAtom(containingAtom.uid);
            raeController.SetLinkToRigidbodyObject(rb);
            raeController.currentPositionState = FreeControllerV3.PositionState.ParentLink;
        }

        // ----- Sample preload / unload -----
        // VaM streams audio off disk; pre-queueing every sample for the chosen
        // surface avoids a stall on the first step. RemoveClips() unloads the
        // previous surface so we don't keep dozens of clips resident forever.

        public void RaePreloadSound(string f)
        {
            string path = Utils.GetPackagePath(this) + SoundsRoot + f;
            string localPath = SuperController.singleton.NormalizeLoadPath(path);
            NamedAudioClip nac = URLAudioClipManager.singleton.GetClip(localPath);
            if (nac == null)
            {
                URLAudioClip clip = URLAudioClipManager.singleton.QueueClip(SuperController.singleton.NormalizeMediaPath(path));
                URLAudioClipManager.singleton.GetClip(clip.uid);
            }
        }

        public void RaeRemovedSound(string f)
        {
            string path = Utils.GetPackagePath(this) + SoundsRoot + f;
            string localPath = SuperController.singleton.NormalizeLoadPath(path);
            NamedAudioClip nac = URLAudioClipManager.singleton.GetClip(localPath);
            if (nac != null)
                URLAudioClipManager.singleton.RemoveClip(nac);
        }

        // Called whenever the user picks a different surface from the dropdown.
        // Always unloads the previous surface's clips first, then preloads the
        // new one (unless they picked "None", in which case we just unload).
        protected void DummyChooserCallback(string s)
        {
            RemoveClips();
            if (s == "None") return;
            int count = sounds[s];
            for (int i = 1; i <= count; i++)
                RaePreloadSound(SamplePath(s, i));
        }

        void RemoveClips()
        {
            foreach (var item in sounds)
                for (int i = 1; i <= item.Value; i++)
                    RaeRemovedSound(SamplePath(item.Key, i));
        }

        // Pick a random sample from the currently-selected surface and return
        // its package-relative path (relative to SoundsRoot).
        string GetRandomSound()
        {
            int count = sounds[soundChooser.val];
            int rand = Random2.Range(1, count + 1);
            return SamplePath(soundChooser.val, rand);
        }

        // Layout on disk: <SoundsRoot>/<surface>/<surface> (N).wav
        // e.g. "Custom/Sounds/FootstepsSFX/boots/boots (3).wav"
        static string SamplePath(string surface, int index)
        {
            return surface + "/" + surface + " (" + index + ").wav";
        }

        // ----- Playback -----

        public AudioSourceControl RaeAudioSource(string id)
        {
            Atom raeAudio = SuperController.singleton.GetAtomByUid(id);
            if (raeAudio == null) return null;
            return raeAudio.GetStorableByID("AudioSource") as AudioSourceControl;
        }

        public void RaeSound(string audiosourceId, string f)
        {
            if (IsDisabled()) return;
            if (SuperController.singleton.GetAtomByUid(audiosourceId) == null) return;

            AudioSourceControl raeAudioSource = RaeAudioSource(audiosourceId);
            if (raeAudioSource == null) return;

            string path = Utils.GetPackagePath(this) + SoundsRoot + f;
            string localPath = SuperController.singleton.NormalizeLoadPath(path);
            NamedAudioClip nac = URLAudioClipManager.singleton.GetClip(localPath);

            // If preload didn't happen (e.g. a step fired before DummyChooserCallback
            // finished queuing the surface's clips), queue this one on demand.
            if (nac == null)
            {
                URLAudioClip clip = URLAudioClipManager.singleton.QueueClip(SuperController.singleton.NormalizeMediaPath(path));
                nac = URLAudioClipManager.singleton.GetClip(clip.uid);
            }

            if (nac != null)
            {
                raeAudioSource.audioSource.Stop();
                raeAudioSource.audioSource.clip = nac.sourceClip;
                raeAudioSource.audioSource.Play();
            }
        }
    }
}
