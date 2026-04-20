# FootstepsExtended

A footstep audio plugin for [Virt-a-Mate](https://virtamate.com/). Detects when a Person atom's feet make contact with surfaces and plays surface-appropriate sound samples — boots on wood, bare feet on carpet, sneakers on concrete, and so on.

> **Credits:** the original *Footsteps* plugin and audio samples are by **SPQR** (CC BY 4.0). This package is a rework of that plugin maintained by Lapiro. All footstep audio shipped here originates from SPQR's library. See [LICENSE](LICENSE) for full attribution.

## Features

- **24 built-in surfaces** — boots, barefoot, socks, sneak, carpet, concrete, dirt, grass, gravel, gritty, hard, heels (×5), ice, squeaky, step, wood (×3), and more
- **Per-foot audio sources** — Unity `AudioSource` components attached directly to the `lFoot` / `rFoot` rigidbodies. No scene clutter, no extra atoms to clean up
- **3D spatial audio** — sounds play from the foot's actual position with proper distance falloff
- **Pitch jitter** — small random pitch variation (default ±15%) per step so repeated samples don't sound mechanically identical
- **Per-foot cooldown** — left and right feet have independent cooldowns so a double-foot landing fires both sounds
- **Anti-double-fire global gap** — small global guard (default 0.1s) absorbs Unity's same-frame ripple collisions so one impact doesn't accidentally fire on both feet
- **Live step indicators** — separate Left Foot / Right Foot status boxes flash green on each step
- **Test buttons** — fire either foot manually so you can A/B sound surfaces without walking the character
- **EventTriggers** — wire arbitrary VaM actions to fire on Any Step / Left Step / Right Step (e.g. spawn a particle, run a Timeline trigger)
- **Custom samples** — drop your own `.wav` or `.mp3` files into any surface folder, or create new surface folders, no code changes required (see *Adding your own sounds* below)
- **Debug log toggle** — verbose timestamped diagnostics for troubleshooting collision detection or audio playback

## Install

1. Drop `Lapiro.FootstepsExtended.<N>.var` into your VaM `AddonPackages/` folder
2. In VaM, on a Person atom: **Plugins** tab → **Add Plugin** → pick `Lapiro.FootstepsExtended` → `Footsteps.cslist`
3. In the plugin UI, pick a surface from the **Sounds** dropdown
4. Walk the character around (or click **Test Left** / **Test Right** to verify it works)

## UI

```
LEFT (sound output)            RIGHT (collision & dispatch)
─────────────────────          ─────────────────────────────
Sounds: [boots ▾]              Impact Min      0.05
Volume          0.50           Impact Max     10.00
Pitch           1.00           Cooldown        0.30
Pitch Jitter    0.15           Min Step Gap    0.10
Spatial Blend   1.00           Trigger On Step
[ Test Left  ]                 Trigger On Left
[ Test Right ]                 Trigger On Right
☐ Debug log                    ┌── Left Foot  ──┐
                               └────────────────┘
                               ┌── Right Foot ──┐
                               └────────────────┘
```

| Control | What it does |
|---|---|
| **Sounds** | Pick a surface, or "None" to disable audio (collision detection still runs) |
| **Volume** | AudioSource volume scale (0–2) |
| **Pitch** | Base pitch (0.5–2). Tune for character size — smaller character → slightly higher |
| **Pitch Jitter** | Random ±N pitch variation per step (0–0.3). Keeps repeated samples from sounding identical |
| **Spatial Blend** | 0 = stereo (always centered), 1 = fully 3D-positioned at the foot |
| **Test Left / Test Right** | Manually fire a step on the chosen foot. Bypasses cooldown |
| **Impact Min / Max** | Velocity range that counts as a step. Below Min: too soft. Above Max: probably a glitch |
| **Cooldown** | How long after a step before THE SAME foot can fire again |
| **Min Step Gap** | Tiny global gap after any step. Absorbs Unity's same-frame ripple collisions |
| **Trigger On Step / Left / Right** | Open VaM's trigger editor — wire any VaM action to fire on a step |
| **Debug log** | When on, every key event prints to VaM's message log with timestamps |

## Adding your own sounds

Audio lives at `Custom/Sounds/FootstepsSFX/<surface>/*.wav` (or `*.mp3`) inside the package. The plugin auto-discovers everything at load time:

- **Add samples to an existing surface** — drop new `.wav` or `.mp3` files into the surface's folder, then re-pick that surface from the dropdown. Naming doesn't matter; any `.wav` or `.mp3` file is picked up
- **Add a new surface** — create a new folder under `FootstepsSFX/`, drop in your samples, reload the plugin. The new surface appears in the dropdown
- **Replace samples** — just overwrite the files. Re-pick the surface to flush the cache

Sample length isn't enforced, but anything much longer than ~1s will overlap with subsequent steps when picked, which usually sounds wrong. Aim for 0.2–0.5s per sample.

## Project layout

```
Project_Footsetps/
├── Footsteps.cslist                    # plugin manifest (paths relative to release root)
├── src/
│   ├── Footsteps.Core.cs               # fields, Init, lifecycle (Update / OnDestroy / OnAtomRename / GetJSON / ...)
│   ├── Footsteps.UI.cs                 # storable callbacks, filter parsing
│   ├── Footsteps.Audio.cs              # AudioSource attach, surface/clip discovery, playback
│   ├── Footsteps.Triggers.cs           # collision detection, trigger firing, foot-side dispatch
│   ├── Helpers/
│   │   └── TriggerCollide.cs           # Unity MonoBehaviour for collision events
│   └── lib/
│       └── MacGruber_Utils.cs          # MacGruber's UI / EventTrigger helpers
├── Sounds/FootstepsSFX/                # all sample audio (each surface in its own folder)
├── .github/
│   ├── meta.template.json              # template baked into the .var as meta.json
│   └── VAM_VERSION                     # current package version (integer)
├── build-release.ps1                   # git archive HEAD → sibling release folder
├── build-package.ps1                   # release → meta-staged → zipped .var → AddonPackages
├── LICENSE
└── README.md
```

The four `Footsteps.*.cs` files declare `partial class Footsteps : MVRScript` — they compile as a single class. The split is purely organizational.

## Credits

- **SPQR** — original Footsteps plugin and all bundled audio samples, licensed CC BY 4.0. [Patreon](https://www.patreon.com/SPQR_aeternum)
- **MacGruber** — `MacGruber_Utils.cs` (UI / EventTrigger helpers)
- **geesp0t** — collision-trigger reference (per upstream credits)
- **AcidBubbles** — collision groundwork (per upstream credits)
- **Lapiro** — this rework

## License

[Creative Commons Attribution 4.0 International (CC BY 4.0)](https://creativecommons.org/licenses/by/4.0/) — see [LICENSE](LICENSE) for full attribution.
