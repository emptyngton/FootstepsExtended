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
        // Cached at Init and re-synced on atom rename so the spawned audio-source
        // child atoms ("<person>.footsteps.left/right") can be tracked.
        string atomid;

        // Two side-specific child AudioSource atoms get spawned next to the feet
        // and parent-linked in FootstepsInit; these triggers fire when a step is
        // detected so users can hook custom behaviour from the trigger panel.
        EventTrigger myEventTrigger;       // any step
        EventTrigger myEventTriggerLeft;   // left-foot step
        EventTrigger myEventTriggerRight;  // right-foot step

        // Step-detection thresholds and cooldown. Cooldown gates `myClock`
        // so we don't fire twice per heel-strike when the foot rebounds.
        JSONStorableFloat treshold1;
        JSONStorableFloat treshold2;
        JSONStorableFloat jfloat2;
        float collisionCooldown = 0.5f;
        float myClock = -1.0f;

        // The currently selected sound surface ("None" / "boots" / "carpet" / ...).
        JSONStorableStringChooser soundChooser;

        // Map of surface name -> number of audio samples shipped for that surface.
        // Used both to populate the dropdown in Init and to randomise sample
        // selection at play time. Numbers must match what's actually under
        // Custom/Sounds/FootstepsSFX/<key>/<key> (N).wav.
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

        // Set true once FootstepsInit has spawned the two child AudioSource atoms.
        // UpdateSecond() retries until this flips, so a freshly-loaded scene
        // self-heals if the atoms aren't present yet at plugin Init time.
        bool initiated = false;

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

            // --- Sound surface picker ---
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

            // --- Sliders (right column) ---
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
            InvokeRepeating("UpdateSecond", 1f, 1f);
            RefreshColliders();
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

        void UpdateSecond()
        {
            if (IsDisabled()) return;
            if (!initiated)
                FootstepsInit();
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

        // Keep the spawned audio-source child atoms in sync when the user renames
        // the host Person atom; otherwise their UIDs go stale and FootstepsInit
        // would spawn a second pair instead of finding the existing ones.
        void OnAtomRename(string oldid, string newid)
        {
            myEventTrigger.SyncAtomNames();
            myEventTriggerLeft.SyncAtomNames();
            myEventTriggerRight.SyncAtomNames();
            if (variableColliderId.val == oldid)
                variableColliderId.val = newid;

            if (atomid != oldid) return;
            atomid = newid;

            var leftatom = SuperController.singleton.GetAtomByUid(oldid + ".footsteps.left");
            var rightatom = SuperController.singleton.GetAtomByUid(oldid + ".footsteps.right");
            if (leftatom != null)
                SuperController.singleton.RenameAtom(leftatom, newid + ".footsteps.left");
            if (rightatom != null)
                SuperController.singleton.RenameAtom(rightatom, newid + ".footsteps.right");
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
            CancelInvoke();
            Utils.OnDestroyUI();
            OnDisable();
            myEventTrigger.Remove();
            myEventTriggerLeft.Remove();
            myEventTriggerRight.Remove();
        }
    }
}
