using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random2 = UnityEngine.Random;
using MacGruber;
using MVR.FileManagementSecure;

namespace Lapiro
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
        // Surface + clip discovery
        // ---------------------------------------------------------------------
        // No naming convention: each surface is just a folder under SoundsRoot,
        // each .wav file in it is a sample. Drop in / remove / rename files
        // freely; the plugin enumerates them at load time.
        //
        // _surfaceClips caches per-surface file lists so we don't hit the VFS
        // on every PlayFootSound. Cleared and refilled when the user picks a
        // surface from the dropdown, so re-picking the same surface picks up
        // any files added since plugin load.

        Dictionary<string, string[]> _surfaceClips = new Dictionary<string, string[]>();

        // Enumerate folder names under SoundsRoot — each becomes a dropdown
        // entry. Sorted alphabetically for stable UI ordering.
        List<string> DiscoverSurfaces()
        {
            var result = new List<string>();
            string root = Utils.GetPackagePath(this) + SoundsRoot;
            string[] dirs;
            try { dirs = FileManagerSecure.GetDirectories(root); }
            catch { dirs = new string[0]; }

            foreach (var d in dirs)
            {
                string trimmed = d.TrimEnd('/', '\\');
                int slash = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
                string name = slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        // Get (and cache) the list of full clip paths for one surface.
        string[] GetSurfaceClips(string surface)
        {
            string[] cached;
            if (_surfaceClips.TryGetValue(surface, out cached)) return cached;

            string dir = Utils.GetPackagePath(this) + SoundsRoot + surface;
            string[] files;
            try
            {
                var wavs = FileManagerSecure.GetFiles(dir, "*.wav");
                var mp3s = FileManagerSecure.GetFiles(dir, "*.mp3");
                files = wavs.Concat(mp3s).ToArray();
            }
            catch { files = new string[0]; }

            _surfaceClips[surface] = files;
            Debug(string.Format("Surface '{0}': discovered {1} clip(s)", surface, files.Length));
            return files;
        }

        // ---------------------------------------------------------------------
        // Sample preload / unload (path-based, plugin-scoped cache)
        // ---------------------------------------------------------------------
        // Deliberately NOT using URLAudioClipManager: it persists loaded clips
        // into scene.json, so entries stick around after the plugin is removed
        // and users end up with ghost audio refs in the Scene Audio Control
        // list. We read the file bytes directly and keep raw AudioClip objects
        // on this dictionary — lifetime is tied to the plugin, and OnDestroy
        // destroys every clip via UnloadAllSurfaces().

        Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>();

        void PreloadPath(string fullPath)
        {
            if (_clipCache.ContainsKey(fullPath)) return;
            var clip = LoadClipFromDisk(fullPath);
            if (clip != null) _clipCache[fullPath] = clip;
        }

        void UnloadPath(string fullPath)
        {
            AudioClip clip;
            if (!_clipCache.TryGetValue(fullPath, out clip)) return;
            _clipCache.Remove(fullPath);
            if (clip != null) UnityEngine.Object.Destroy(clip);
        }

        // NAudioPlayer / WAV are VaM built-ins (same helpers CheesyFX and
        // AcidBubbles Voxta use). .mp3 and .wav only — .ogg has no built-in
        // decoder and users can trivially convert.
        AudioClip LoadClipFromDisk(string fullPath)
        {
            try
            {
                var bytes = FileManagerSecure.ReadAllBytes(fullPath);
                return fullPath.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                    ? NAudioPlayer.AudioClipFromMp3Data(bytes)
                    : NAudioPlayer.AudioClipFromWAV(new WAV(bytes));
            }
            catch (Exception e)
            {
                SuperController.LogError("Footsteps: failed to load '" + fullPath + "': " + e.Message);
                return null;
            }
        }

        // Called whenever the user picks a different surface from the dropdown.
        // Always unloads previously-cached clips first, then re-enumerates the
        // newly-picked surface (so files added since last selection get picked
        // up) and preloads them all.
        protected void DummyChooserCallback(string s)
        {
            UnloadAllSurfaces();
            if (s == "None") return;

            // Force re-enumeration of the picked surface — picks up any files
            // added/removed since the last cache. Other surfaces' caches stay.
            _surfaceClips.Remove(s);
            foreach (var path in GetSurfaceClips(s))
                PreloadPath(path);
        }

        void UnloadAllSurfaces()
        {
            foreach (var kv in _surfaceClips)
                foreach (var path in kv.Value)
                    UnloadPath(path);
        }

        // Pick a random clip path from the currently-selected surface. Returns
        // null on empty / missing surface, in which case PlayFootSound bails.
        string GetRandomClipPath()
        {
            if (string.IsNullOrEmpty(soundChooser.val) || soundChooser.val == "None") return null;
            var clips = GetSurfaceClips(soundChooser.val);
            if (clips == null || clips.Length == 0) return null;
            return clips[Random2.Range(0, clips.Length)];
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
        // gate accumulates audibly.
        //
        // Volume / pitch / spatial blend are read from JSONStorables on every
        // step, so slider edits take effect immediately.

        void PlayFootSound(AudioSource source, string clipPath)
        {
            if (IsDisabled()) { Debug("PLAY BAILED: plugin disabled"); return; }
            if (source == null) { Debug("PLAY BAILED: AudioSource null (feet not attached yet)"); return; }
            if (string.IsNullOrEmpty(clipPath)) { Debug("PLAY BAILED: no clip path (empty surface?)"); return; }

            AudioClip audioClip;
            if (!_clipCache.TryGetValue(clipPath, out audioClip) || audioClip == null)
            {
                // Lazy-load fallback: shouldn't normally fire because preload
                // runs synchronously on surface-change, but covers the case of
                // a clip file appearing mid-session or a prior load failure.
                audioClip = LoadClipFromDisk(clipPath);
                if (audioClip != null) _clipCache[clipPath] = audioClip;
            }
            if (audioClip == null)
            {
                Debug(string.Format("PLAY BAILED: clip not loaded for '{0}'", clipPath));
                return;
            }

            // ---- Capture pre-play state for diagnostics ----
            bool isLeft = source == leftFootAudio;
            float now = Time.time;
            float lastPlayed = isLeft ? lastLeftPlayTime : lastRightPlayTime;
            float sinceLastPlay = lastPlayed < 0f ? -1f : now - lastPlayed;
            bool wasPlaying = source.isPlaying;
            float cutAt = wasPlaying ? source.time : 0f;
            float prevDur = (wasPlaying && source.clip != null) ? source.clip.length : 0f;
            float newDur = audioClip.length;

            // Per-step pitch jitter — without it, hearing the same sample twice
            // in a row is an obvious tell.
            float j = pitchJitter.val;
            source.pitch = pitch.val * (1f + Random2.Range(-j, j));
            source.spatialBlend = spatialBlend.val;
            source.volume = volume.val;
            source.Stop();
            source.clip = audioClip;
            source.Play();

            if (isLeft) lastLeftPlayTime = now; else lastRightPlayTime = now;

            string sinceStr = sinceLastPlay < 0f ? "first" : string.Format("+{0:F2}s", sinceLastPlay);
            string interruptStr = wasPlaying
                ? string.Format("INTERRUPTED@{0:F2}/{1:F2}s ({2:P0} into prev clip)", cutAt, prevDur, prevDur > 0f ? cutAt / prevDur : 0f)
                : "(prev finished)";

            // Show just the file name in the log (the full VFS path is noisy
            // and not useful for diagnosis).
            string clipName = clipPath;
            int lastSlash = Math.Max(clipName.LastIndexOf('/'), clipName.LastIndexOf('\\'));
            if (lastSlash >= 0) clipName = clipName.Substring(lastSlash + 1);

            Debug(string.Format("PLAY {0} ← '{1}' dur={2:F2}s sinceLast={3} {4}",
                isLeft ? "lFoot" : "rFoot", clipName, newDur, sinceStr, interruptStr));
        }
    }
}
