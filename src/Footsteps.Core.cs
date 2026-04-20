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

        // Step-detection thresholds and post-step cooldown gate (`myClock`
        // counts down so we don't fire twice per heel-strike when the foot
        // rebounds against the floor).
        JSONStorableFloat treshold1;
        JSONStorableFloat treshold2;
        JSONStorableFloat jfloat2;
        float collisionCooldown = 0.5f;
        float myClock = -1.0f;

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

        // Tiny "Right Foot" / "Left Foot" indicator that flashes green on hit.
        JSONStorableString collisionsPreview;
        UIDynamicTextField dtext4;

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

            variableColliderGroup = new JSONStorableString("Collider Group", "Slate|Floor|WoodPanel|ReflectiveSlate|ReflectiveWoodPanel", UpdateVarValue);
            variableColliderGroup.storeType = JSONStorableParam.StoreType.Full;
            RegisterString(variableColliderGroup);

            variableColliderId = new JSONStorableString("Collider Atom", "", UpdateVarValue);
            variableColliderId.storeType = JSONStorableParam.StoreType.Full;
            RegisterString(variableColliderId);

            variableColliderElement = new JSONStorableString("Collider Element", "", UpdateVarValue);
            variableColliderElement.storeType = JSONStorableParam.StoreType.Full;
            RegisterString(variableColliderElement);

            Utils.SetupSpacer(this, 66, false);

            // --- Audio sliders (right column) ---
            volume = new JSONStorableFloat("Volume", 1f, BlendCallback, 0f, 2f, true);
            volume.storeType = JSONStorableParam.StoreType.Full;
            RegisterFloat(volume);
            CreateSlider(volume, true);

            pitch = new JSONStorableFloat("Pitch", 1f, BlendCallback, 0.5f, 2f, true);
            pitch.storeType = JSONStorableParam.StoreType.Full;
            RegisterFloat(pitch);
            CreateSlider(pitch, true);

            pitchJitter = new JSONStorableFloat("Pitch Jitter", 0.05f, BlendCallback, 0f, 0.3f, true);
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

            // --- Live "which foot just hit?" indicator ---
            collisionsPreview = new JSONStorableString("collisionsPreview", "");
            dtext4 = CreateTextField(collisionsPreview, true);
            dtext4.UItext.supportRichText = true;
            dtext4.backgroundColor = new Color(1f, 0.33f, 0.33f, 0.7f);
            dtext4.textColor = new Color(1f, 1f, 1f, 0.9f);
            dtext4.height = 100f;

            updateVariables();
            SimpleTriggerHandler.LoadAssets();
        }

        void Start()
        {
            RefreshColliders();
            AttachFootAudioSources();
        }

        void Update()
        {
            myEventTrigger.Update();
            myEventTriggerLeft.Update();
            myEventTriggerRight.Update();
            if (myClock < 0.0f)
                return;

            myClock -= Time.deltaTime;
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
