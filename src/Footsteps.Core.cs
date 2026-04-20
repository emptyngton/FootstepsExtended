using System;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using SimpleJSON;
using MacGruber;
using customevent;

namespace SPQR
{
    public partial class Footsteps : MVRScript
    {
        // Identity of the Person atom this plugin instance is attached to.
        // Cached at Init and re-synced on atom rename so the EventTrigger
        // panels keep their references to this atom valid.
        string atomid;

        // EventTrigger panels exposed on the right column. Users can wire
        // arbitrary VaM actions to fire whenever a step is detected.
        EventTrigger myEventTrigger;       // any step
        EventTrigger myEventTriggerLeft;   // left-foot step
        EventTrigger myEventTriggerRight;  // right-foot step

        // Step-detection thresholds and post-step cooldown gate. Each foot has
        // its OWN cooldown clock — a double-foot landing (jump, two-footed
        // stomp) needs both feet to fire, not just one. The "Cooldown" slider
        // sets the per-foot cooldown duration; the leftClock/rightClock fields
        // tick down independently in Update().
        JSONStorableFloat treshold1;
        JSONStorableFloat treshold2;
        JSONStorableFloat jfloat2;
        float collisionCooldown = 0.3f;
        float leftClock = -1.0f;
        float rightClock = -1.0f;

        // Audio knobs surfaced to the user. Read on every PlayOneShot so
        // slider moves take effect on the next step without a callback.
        JSONStorableFloat volume;        // [0..2]
        JSONStorableFloat pitch;         // [0.5..2]
        JSONStorableFloat pitchJitter;   // [0..0.3] — random ± fraction applied per step
        JSONStorableFloat spatialBlend;  // [0..1] — 0=stereo, 1=fully 3D-positioned at the foot

        // Unity AudioSource components attached directly to the lFoot / rFoot
        // rigidbody GameObjects in AttachFootAudioSources(). Destroyed in
        // OnDestroy so we don't leak components into the scene.
        AudioSource leftFootAudio;
        AudioSource rightFootAudio;

        // Currently selected sound surface ("None" / "boots" / "carpet" / ...).
        JSONStorableStringChooser soundChooser;

        // Map of surface name -> number of audio samples shipped for that
        // surface. Used both to populate the dropdown in Init and to randomise
        // sample selection at play time. Numbers must match what's actually
        // under Custom/Sounds/FootstepsSFX/<key>/<key> (N).wav.
        Dictionary<string, int> sounds = new Dictionary<string, int>(){
            {"sneak", 20},
            {"Socks", 18},
            {"bootsfly", 24},
            {"boots", 9},
            {"carpet", 6},
            {"concrete", 11},
            {"dirt", 5},
            {"grass", 1},
            {"grass2", 6},
            {"gravel", 1},
            {"gritty", 10},
            {"hard", 11},
            {"heels1", 13},
            {"heels2", 3},
            {"heels3", 1},
            {"heels4", 10},
            {"ice", 2},
            {"squeaky", 30},
            {"step", 2},
            {"step2", 4},
            {"wood1", 17},
            {"wood2", 4},
            {"wood3", 20},
            {"barefoot", 11}
        };

        // Pipe-separated filter inputs (as text). Edited via the UI fields.
        // updateVariables() splits each into its `*Elements` companion array
        // that ObserveTrigger / RaeClickTouch consult on every collision.
        JSONStorableString variableThisElement;
        JSONStorableString variableColliderGroup;
        JSONStorableString variableColliderId;
        JSONStorableString variableColliderElement;

        string[] variableThisElements;
        string[] variableColliderGroups;
        string[] variableColliderIds;
        string[] variableColliderElements;

        // Two static "Left Foot" / "Right Foot" indicators that flash green
        // when their respective foot fires a step. Independent so a double-foot
        // landing visibly flashes both at once.
        UIDynamicTextField leftFootIndicator;
        UIDynamicTextField rightFootIndicator;

        static readonly Color IndicatorIdleColor = new Color(1f, 0.33f, 0.33f, 0.7f);
        static readonly Color IndicatorHitColor  = new Color(0.33f, 1f, 0.33f, 0.7f);

        // Toggle that, when on, prints diagnostic messages to VaM's message log
        // for every key event in the pipeline (subscribe count, attach success,
        // collision filter pass/fail, sound playback). Off by default — only
        // useful when something stops working.
        JSONStorableBool debugLog;

        public override void Init()
        {
            if (containingAtom == null || containingAtom.type != "Person")
            {
                SuperController.LogError("Footsteps plugin must be added on a Person atom!");
                return;
            }

            SuperController.singleton.onAtomUIDRenameHandlers += OnAtomRename;

            Utils.OnInitUI(CreateUIElement);

            atomid = containingAtom.uid;

            // --- Header ---
            JSONStorableString logo = new JSONStorableString("logostring", "\n<size=40><b>Footsteps</b></size>");
            UIDynamicTextField logotext = CreateTextField(logo);
            logotext.UItext.supportRichText = true;
            logotext.backgroundColor = new Color(0f, 0f, 0f, 0f);
            logotext.textColor = new Color(0.42f, 0.03f, 0.51f, 1f);
            logotext.height = 60f;
            logotext.UItext.alignment = TextAnchor.UpperLeft;

            JSONStorableString logor = new JSONStorableString("logospace", "\n<size=30><b>Extended</b></size>");
            UIDynamicTextField logortext = CreateTextField(logor, true);
            logortext.UItext.supportRichText = true;
            logortext.backgroundColor = new Color(0f, 0f, 0f, 0f);
            logortext.textColor = new Color(0.42f, 0.03f, 0.51f, 1f);
            logortext.height = 60f;
            logortext.UItext.alignment = TextAnchor.UpperRight;

            Utils.SetupSpacer(this, 10, false);
            Utils.SetupSpacer(this, 10, true);

            // --- Sound surface picker (left column) ---
            List<string> choices = new List<string>();
            choices.Add("None");
            foreach (var item in sounds)
                choices.Add(item.Key);

            soundChooser = new JSONStorableStringChooser("Sounds", choices, "None", "Sounds", DummyChooserCallback);
            RegisterStringChooser(soundChooser);
            UIDynamicPopup udp = CreateScrollablePopup(soundChooser, false);
            udp.labelWidth = 300f;

            // --- Filter fields (left column) ---
            variableThisElement = new JSONStorableString("Element:", "rFoot|lFoot", UpdateVarValue);
            variableThisElement.storeType = JSONStorableParam.StoreType.Full;
            RegisterString(variableThisElement);

            // Collider Group filter is empty by default — matches any atom
            // type. SPQR's original default ("Slate|Floor|WoodPanel|...") was
            // exact-match against the parent atom's TYPE, which broke the
            // common case of a panel wrapped inside an Empty parent atom or
            // a Subscene (the wrapper's type is "Empty"/"SubScene", not
            // "WoodPanel"). Users can opt back into restriction by typing
            // pipe-separated atom types here.
            variableColliderGroup = new JSONStorableString("Collider Group", "", UpdateVarValue);
            variableColliderGroup.storeType = JSONStorableParam.StoreType.Full;
            RegisterString(variableColliderGroup);

            variableColliderId = new JSONStorableString("Collider Atom", "", UpdateVarValue);
            variableColliderId.storeType = JSONStorableParam.StoreType.Full;
            RegisterString(variableColliderId);

            variableColliderElement = new JSONStorableString("Collider Element", "", UpdateVarValue);
            variableColliderElement.storeType = JSONStorableParam.StoreType.Full;
            RegisterString(variableColliderElement);

            // Debug toggle (left column, under the filter fields).
            debugLog = new JSONStorableBool("Debug log", false);
            RegisterBool(debugLog);
            CreateToggle(debugLog, false);

            Utils.SetupSpacer(this, 66, false);

            // --- Audio sliders (right column) ---
            volume = new JSONStorableFloat("Volume", 0.5f, BlendCallback, 0f, 2f, true);
            volume.storeType = JSONStorableParam.StoreType.Full;
            RegisterFloat(volume);
            CreateSlider(volume, true);

            pitch = new JSONStorableFloat("Pitch", 1f, BlendCallback, 0.5f, 2f, true);
            pitch.storeType = JSONStorableParam.StoreType.Full;
            RegisterFloat(pitch);
            CreateSlider(pitch, true);

            pitchJitter = new JSONStorableFloat("Pitch Jitter", 0.15f, BlendCallback, 0f, 0.3f, true);
            pitchJitter.storeType = JSONStorableParam.StoreType.Full;
            RegisterFloat(pitchJitter);
            CreateSlider(pitchJitter, true);

            spatialBlend = new JSONStorableFloat("Spatial Blend", 1f, BlendCallback, 0f, 1f, true);
            spatialBlend.storeType = JSONStorableParam.StoreType.Full;
            RegisterFloat(spatialBlend);
            CreateSlider(spatialBlend, true);

            // --- Collision sliders (right column) ---
            treshold1 = new JSONStorableFloat("Impact Min", 0.05f, BlendCallback, 0f, 10f, true);
            treshold1.storeType = JSONStorableParam.StoreType.Full;
            RegisterFloat(treshold1);
            CreateSlider(treshold1, true);

            treshold2 = new JSONStorableFloat("Impact Max", 10f, BlendCallback, 0f, 10f, true);
            treshold2.storeType = JSONStorableParam.StoreType.Full;
            RegisterFloat(treshold2);
            CreateSlider(treshold2, true);

            jfloat2 = new JSONStorableFloat("Cooldown", collisionCooldown, BlendCallback, 0f, 10f, true);
            jfloat2.storeType = JSONStorableParam.StoreType.Full;
            RegisterFloat(jfloat2);
            CreateSlider(jfloat2, true);

            // --- Trigger panels (right column) ---
            myEventTrigger = new EventTrigger(this, "TriggerOnUpdate");
            Utils.SetupButton(this, "Trigger On Step", myEventTrigger.OpenPanel, true);

            myEventTriggerLeft = new EventTrigger(this, "TriggerOnUpdateLeft");
            Utils.SetupButton(this, "Trigger On Left", myEventTriggerLeft.OpenPanel, true);

            myEventTriggerRight = new EventTrigger(this, "TriggerOnUpdateRight");
            Utils.SetupButton(this, "Trigger On Right", myEventTriggerRight.OpenPanel, true);

            Utils.SetupSpacer(this, 66, true);

            // --- Per-foot step indicators (right column) ---
            // Static labels — the position alone tells you which foot. Background
            // flashes green on hit via FlashIndicator() in Footsteps.Triggers.cs.
            leftFootIndicator  = CreateStepIndicator("Left Foot");
            rightFootIndicator = CreateStepIndicator("Right Foot");

            updateVariables();
            SimpleTriggerHandler.LoadAssets();
        }

        UIDynamicTextField CreateStepIndicator(string label)
        {
            var s = new JSONStorableString(label + "Indicator", "<size=20><b>" + label + "</b></size>");
            var tf = CreateTextField(s, true);
            tf.UItext.supportRichText = true;
            tf.UItext.alignment = TextAnchor.MiddleCenter;
            tf.backgroundColor = IndicatorIdleColor;
            tf.textColor = new Color(1f, 1f, 1f, 0.9f);
            tf.height = 80f;
            return tf;
        }

        void Start()
        {
            // Try once immediately — usually succeeds on a freshly-built scene
            // where the Person's body parts are already loaded by Start time.
            RefreshColliders();
            AttachFootAudioSources();

            // ...but on scene LOAD (vs build-from-scratch), the Person atom can
            // still be assembling its rigidbodies when Start fires, so the first
            // pass may have found nothing to subscribe to. Retry every second
            // until both feet have AudioSources, then stop polling.
            InvokeRepeating(nameof(EnsureSetupReady), 1f, 1f);
        }

        void EnsureSetupReady()
        {
            if (FeetReady)
            {
                Debug("EnsureSetupReady: feet ready, stopping retry loop");
                CancelInvoke(nameof(EnsureSetupReady));
                return;
            }
            Debug("EnsureSetupReady: feet not ready, retrying...");
            RefreshColliders();
            AttachFootAudioSources();
        }

        // Conditional log helper. Bypasses IsDisabled() on purpose — debug logs
        // are most useful precisely when the plugin appears to be doing nothing.
        // Orphan instances (containingAtom destroyed) stay completely silent.
        void Debug(string msg)
        {
            if (debugLog == null || !debugLog.val) return;
            if (containingAtom == null) return;
            SuperController.LogMessage("[Footsteps:" + containingAtom.uid + "] " + msg);
        }

        void Update()
        {
            myEventTrigger.Update();
            myEventTriggerLeft.Update();
            myEventTriggerRight.Update();

            // Tick down both per-foot cooldowns independently. Stop decrementing
            // once a clock hits 0 so it doesn't drift arbitrarily negative.
            if (leftClock > 0f)  leftClock  -= Time.deltaTime;
            if (rightClock > 0f) rightClock -= Time.deltaTime;
        }

        public bool IsDisabled()
        {
            if (SuperController.singleton.freezeAnimationToggle.isOn) return true;
            if (!enabledJSON.val) return true;
            return false;
        }

        void OnEnable()
        {
            RefreshColliders();
            updateVariables();
        }

        void OnDisable()
        {
            // Defensive: containingAtom may already be destroyed if the Person
            // atom is being torn down before the plugin's OnDisable fires. Don't
            // NPE; just skip the unsubscribe (the rigidbodies are gone too, so
            // there's nothing to unsubscribe from anyway).
            if (containingAtom == null) return;

            // Detach every TriggerCollide we hooked into; the try/catch covers
            // rigidbodies that never had one attached (e.g. plugin loaded mid-scene).
            containingAtom.GetComponentsInChildren<Rigidbody>().ToList().ForEach(f =>
            {
                try
                {
                    f.gameObject.GetComponent<TriggerCollide>().OnCollide -= ObserveTrigger;
                }
                catch
                {
                }
            });
        }

        void OnReload()
        {
            OnDisable();
        }

        // Just keep the EventTrigger panels' atom-name caches fresh and update
        // the collider-id filter field if it pointed at the renamed atom. No
        // child atoms to rename anymore — the audio sources are Unity components
        // on the foot rigidbodies, not separate atoms.
        void OnAtomRename(string oldid, string newid)
        {
            myEventTrigger.SyncAtomNames();
            myEventTriggerLeft.SyncAtomNames();
            myEventTriggerRight.SyncAtomNames();
            if (variableColliderId.val == oldid)
                variableColliderId.val = newid;
            if (atomid == oldid)
                atomid = newid;
        }

        public override JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false)
        {
            JSONClass jc = base.GetJSON(includePhysical, includeAppearance, forceStore);
            if (includePhysical || forceStore)
            {
                needsStore = true;
                jc[myEventTrigger.Name] = myEventTrigger.GetJSON(base.subScenePrefix);
                jc[myEventTriggerLeft.Name] = myEventTriggerLeft.GetJSON(base.subScenePrefix);
                jc[myEventTriggerRight.Name] = myEventTriggerRight.GetJSON(base.subScenePrefix);
            }
            return jc;
        }

        public override void LateRestoreFromJSON(JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, bool setMissingToDefault = true)
        {
            base.LateRestoreFromJSON(jc, restorePhysical, restoreAppearance, setMissingToDefault);
            if (!base.physicalLocked && restorePhysical && !IsCustomPhysicalParamLocked("trigger"))
            {
                myEventTrigger.RestoreFromJSON(jc, base.subScenePrefix, base.mergeRestore, setMissingToDefault);
                myEventTriggerLeft.RestoreFromJSON(jc, base.subScenePrefix, base.mergeRestore, setMissingToDefault);
                myEventTriggerRight.RestoreFromJSON(jc, base.subScenePrefix, base.mergeRestore, setMissingToDefault);
            }
        }

        void OnDestroy()
        {
            CancelInvoke();
            SuperController.singleton.onAtomUIDRenameHandlers -= OnAtomRename;
            Utils.OnDestroyUI();
            OnDisable();
            myEventTrigger.Remove();
            myEventTriggerLeft.Remove();
            myEventTriggerRight.Remove();

            // Strip the AudioSource components we added so the foot GameObjects
            // are left exactly as we found them.
            if (leftFootAudio != null)  Destroy(leftFootAudio);
            if (rightFootAudio != null) Destroy(rightFootAudio);
        }
    }
}
