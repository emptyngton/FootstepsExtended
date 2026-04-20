# FootstepsExtended

A fork of [SPQR's Footsteps plugin](https://www.patreon.com/SPQR_aeternum) (v2) with four extra footstep sound categories. Original work by SPQR, modifications by Lapiro. Licensed CC BY 4.0 — see [LICENSE](LICENSE).

## What's added on top of SPQR.Footsteps.2

| Category   | Samples |
|------------|---------|
| `sneak`    | 20      |
| `Socks`    | 18      |
| `bootsfly` | 24      |
| `barefoot` | 11      |

That's the only behavioural difference. Everything else — the collision detection, the trigger system, the UI — is unchanged from upstream v2.

## Install

Drop `Lapiro.FootstepsExtended.<N>.var` into your VaM `AddonPackages/` folder. Add to a Person atom via the Plugins tab → Add Plugin → pick the package.

## Project layout

```
Project_Footsetps/
├── Footsteps.cslist                    # plugin manifest (paths relative to release root)
├── src/
│   ├── Footsteps.Core.cs               # fields, Init, lifecycle (Update, OnDestroy, OnAtomRename, GetJSON, ...)
│   ├── Footsteps.UI.cs                 # storable callbacks, CreateTitle, preset load/save
│   ├── Footsteps.Audio.cs              # audio-source atom mgmt, sound preload/play, sound dictionary
│   ├── Footsteps.Triggers.cs           # collision detection, trigger firing, Foot left/right dispatch
│   ├── Helpers/
│   │   └── TriggerCollide.cs           # Unity MonoBehaviour for collision events
│   └── lib/
│       └── MacGruber_Utils.cs          # MacGruber's UI/event-trigger helpers
├── Sounds/FootstepsSFX/                # all sample audio (each category in its own folder)
├── .github/
│   ├── meta.template.json              # template baked into the .var as meta.json
│   └── VAM_VERSION                     # current package version (integer)
├── build-release.ps1                   # git archive HEAD → sibling release folder
├── build-package.ps1                   # release → meta-staged → zip → AddonPackages
├── LICENSE
└── README.md
```

The four `Footsteps.*.cs` files are all `partial class Footsteps : MVRScript` — they compile as a single class. The split is purely organisational; behaviour is byte-identical to SPQR's monolithic `Footsteps.cs` v3 source.

## Build

Requires PowerShell on Windows.

```powershell
# Builds the release sibling folder + packages a .var into VaM's AddonPackages folder.
.\build-package.ps1
```

The script auto-scans `AddonPackages/` for the highest existing `Lapiro.FootstepsExtended.<N>.var` and proposes `N+1`. Press Enter to accept, or type a different number.

## Credits

- **SPQR** — original Footsteps plugin (CC BY 4.0)
- **MacGruber** — `MacGruber_Utils.cs` (UI helpers, EventTrigger)
- **geesp0t** — spanking plugin used as reference for trigger collision (per upstream credits)
- **AcidBubbles** — collision groundwork (per upstream credits)
- **Lapiro** — fork: extra sound categories, project restructure

## License

[Creative Commons Attribution 4.0 International (CC BY 4.0)](https://creativecommons.org/licenses/by/4.0/) — see [LICENSE](LICENSE) for full attribution and modification notes.
