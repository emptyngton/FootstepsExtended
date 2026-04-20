using UnityEngine;
using Random2 = UnityEngine.Random;
using MacGruber;

namespace SPQR
{
    public partial class Footsteps : MVRScript
    {
        const string SoundsRoot = "Custom/Sounds/FootstepsSFX/";

        // ---------------------------------------------------------------------
        // AudioSource attachment
        // ---------------------------------------------------------------------
        // We add a Unity AudioSource component directly to the lFoot/rFoot
        // rigidbodies' GameObjects rather than spawning external AudioSource
        // atoms. Cleaner: no scene clutter, no atom-UID rename juggling, and
        // OnDestroy just calls Destroy() on the components.

        // Idempotent: skips feet that already have an AudioSource attached, so
        // the EnsureSetupReady retry loop can call this every second until the
        // Person body parts finish loading without ever double-attaching.
        void AttachFootAudioSources()
        {
            foreach (var rb in containingAtom.GetComponentsInChildren<Rigidbody>())
            {
                if (rb.name == "lFoot" && leftFootAudio == null)
                    leftFootAudio = ConfigureFootAudio(rb.gameObject);
                else if (rb.name == "rFoot" && rightFootAudio == null)
                    rightFootAudio = ConfigureFootAudio(rb.gameObject);
            }
            Debug("AttachFootAudioSources: lFoot=" + (leftFootAudio != null) + " rFoot=" + (rightFootAudio != null));
        }

        // True only when both feet have an AudioSource attached. Used by the
        // setup-ready retry loop to know when to stop polling.
        bool FeetReady { get { return leftFootAudio != null && rightFootAudio != null; } }

        AudioSource ConfigureFootAudio(GameObject host)
        {
            var src = host.AddComponent<AudioSource>();
            src.spatialize = true;
            src.playOnAwake = false;
            // spatialBlend / pitch / volume are applied per-PlayOneShot, so the
            // user-facing sliders take effect on the very next step without a
            // dedicated setCallbackFunction wired to each slider.
            return src;
        }

        // ---------------------------------------------------------------------
        // Sample preload / unload
        // ---------------------------------------------------------------------
        // VaM streams audio off disk; pre-queueing every sample for the chosen
        // surface avoids a stall on the first step. UnloadAllSurfaces() drops
        // the previous surface's clips so we don't keep dozens of clips
        // resident forever as the user explores surfaces.

        void PreloadSound(string f)
        {
            string path = Utils.GetPackagePath(this) + SoundsRoot + f;
            string localPath = SuperController.singleton.NormalizeLoadPath(path);
            if (URLAudioClipManager.singleton.GetClip(localPath) != null) return;
            URLAudioClipManager.singleton.QueueClip(SuperController.singleton.NormalizeMediaPath(path));
        }

        void UnloadSound(string f)
        {
            string path = Utils.GetPackagePath(this) + SoundsRoot + f;
            string localPath = SuperController.singleton.NormalizeLoadPath(path);
            var nac = URLAudioClipManager.singleton.GetClip(localPath);
            if (nac != null) URLAudioClipManager.singleton.RemoveClip(nac);
        }

        // Called whenever the user picks a different surface from the dropdown.
        // Always unloads the previous surface's clips first, then preloads the
        // new one (unless they picked "None", in which case we just unload).
        protected void DummyChooserCallback(string s)
        {
            UnloadAllSurfaces();
            if (s == "None") return;
            int count = sounds[s];
            for (int i = 1; i <= count; i++)
                PreloadSound(SamplePath(s, i));
        }

        void UnloadAllSurfaces()
        {
            foreach (var item in sounds)
                for (int i = 1; i <= item.Value; i++)
                    UnloadSound(SamplePath(item.Key, i));
        }

        // Layout on disk: <SoundsRoot>/<surface>/<surface> (N).wav
        // e.g. "Custom/Sounds/FootstepsSFX/boots/boots (3).wav"
        static string SamplePath(string surface, int index)
        {
            return surface + "/" + surface + " (" + index + ").wav";
        }

        // Pick a random sample from the currently-selected surface and return
        // its package-relative path (relative to SoundsRoot).
        string GetRandomSound()
        {
            int count = sounds[soundChooser.val];
            int rand = Random2.Range(1, count + 1);
            return SamplePath(soundChooser.val, rand);
        }

        // ---------------------------------------------------------------------
        // Playback
        // ---------------------------------------------------------------------
        // Stop() + .clip = x + Play() on the foot's AudioSource — each new step
        // cleanly interrupts the previous one on the SAME foot. Left and right
        // feet use separate AudioSources so natural walking (alternating feet)
        // still lets their sounds overlap.
        //
        // This is deliberately NOT PlayOneShot: PlayOneShot layers every call
        // on top of the previous, so any step that slips through the cooldown
        // gate (lower cooldown values, or edge-case timing) accumulates
        // audibly — you quickly end up with a pile-up of concurrent voices
        // when stomping fast. With Stop/Play the worst case is a clipped tail
        // on the previous step, which matches the original SPQR behavior.
        //
        // Volume / pitch / spatial blend are read from JSONStorables on every
        // step, so slider edits take effect immediately.

        void PlayFootSound(AudioSource source, string surfaceFile)
        {
            if (IsDisabled()) { Debug("PlayFootSound: bailed (plugin disabled)"); return; }
            if (source == null) { Debug("PlayFootSound: bailed (AudioSource is null — feet not attached yet)"); return; }

            string path = Utils.GetPackagePath(this) + SoundsRoot + surfaceFile;
            string localPath = SuperController.singleton.NormalizeLoadPath(path);
            NamedAudioClip nac = URLAudioClipManager.singleton.GetClip(localPath);

            // Lazy-load fallback: a step may fire before DummyChooserCallback
            // has finished queuing the new surface's samples (queueing is async
            // off disk).
            if (nac == null)
            {
                URLAudioClip clip = URLAudioClipManager.singleton.QueueClip(SuperController.singleton.NormalizeMediaPath(path));
                nac = URLAudioClipManager.singleton.GetClip(clip.uid);
            }
            if (nac == null || nac.sourceClip == null)
            {
                Debug("PlayFootSound: bailed (clip not loaded for path '" + path + "')");
                return;
            }

            // Per-step pitch jitter — without it, hearing the same sample twice
            // in a row is an obvious tell. ±5% default is barely audible per
            // step but huge for variety across many steps.
            float j = pitchJitter.val;
            source.pitch = pitch.val * (1f + Random2.Range(-j, j));
            source.spatialBlend = spatialBlend.val;
            source.volume = volume.val;
            source.Stop();
            source.clip = nac.sourceClip;
            source.Play();
            Debug("PlayFootSound: playing '" + surfaceFile + "' on " + (source == leftFootAudio ? "lFoot" : "rFoot"));
        }
    }
}
