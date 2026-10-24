# SWRP style guide

One look and one feel for the whole game. Every number here comes from code that already exists; where two parts of the
game disagreed, the guide picks one and the disagreement is listed under **Outliers** at the end, with which thread owns
the fix. Owner of this file and of `Code/Feel/`: the "game feel" thread. Change a rule here first, then in code.

Shared code for it:

| Where | What |
|---|---|
| `Code/Feel/_feel.scss` | the palette, type scale, spacing, radius and motion tokens below as SCSS variables, and the `feel-*` motion mixins. `@import "../Feel/_feel.scss";` from any panel stylesheet |
| `Code/Feel/FeelMotion.cs` | the same timings and easing curves for anything animated from C# |
| `Code/Feel/FeelSounds.cs` | the sound set (constants and the surface rules) |

---

## 1. Palette

### Republic (armour, ship, clone gear)

Clone armour is warm white, never pure white; the ship is cool grey durasteel. Both are the models' own PBR textures, so a
lit trooper always reads a little warmer than the hull he stands against.

| Name | Hex | Palette index | Use |
|---|---|---|---|
| Armour | `#ecece6` | `Armor` 1 | clone plates |
| Armour shade | `#cecfcb` | `ArmorShade` 2 | plate edges, recesses |
| Armour dirt | `#b6b0a2` | `ArmorDirt` 3 | wear, veterans |
| Body suit | `#222326` | `BodySuit` 4 | undersuit, gloves (`Glove` 8) |
| Visor | `#121418` | `Visor` 6 | T-visor, never pure black |
| Durasteel | `#989ca0` | `Durasteel` 26 | ship structure |
| Durasteel dark | `#60646a` | `DurasteelDark` 27 | trim, floors (`Floor` 53 `#5c5e64`) |
| Panel | `#babcbe` | `Panel` 28 | wall panels |
| Hull red | `#96221e` | `HullRed` 30 | Republic red stripe (ship and gunships only) |
| Hazard | `#daa01e` | `Hazard` 31 | hazard stripes, pad edges |
| Light white | `#fff8e8` | `LightWhite` 33 / `GlowWhite` 240 | ceiling strips (warm white) |
| Light blue | `#78beff` | `LightBlue` 34 / `GlowBlue` 241 | screens, consoles |

### Battalion accents

Two values per battalion on purpose: the **UI tint** (`Data/Factions.cs` `ColorHex`, brighter so it reads on dark
panels) and the **armour paint** (`ModelSource/bf2_markings.py` colours, a little darker so they don't glow under lights). Use the UI tint
for text, chips, nametags and squad markers; use the armour paint for anything rendered in 3D. Never mix them.

| Battalion | UI tint | Armour paint |
|---|---|---|
| Clone trooper (shinies) | `#d8dde3` | none (plain armour) |
| 501st | `#3a66c4` (was `#2f6fdb`) | `Mark501` `#345ca8` |
| 212th | `#e08a1e` | `Mark212` `#d68028` |
| 104th | `#8a939c` | `Mark104` `#80848c` |
| 327th | `#e3c43a` | `Mark327` `#d6b234` |
| 41st | `#4f8a3c` | `Mark41` `#5c8c48` |
| Coruscant Guard | `#b3262b` | `MarkGuard` `#a82824` |
| (reserved) 91st, 442nd, commando | | `Mark91` `#a02424`, `Mark442` `#466e46`, `MarkCommando` `#786846` |

### Sides

From `Data/Allegiances.cs` (UI tints) and the weapon data.

| Side | UI tint | Bolt | Notes |
|---|---|---|---|
| GAR | `#d8dde3` | `#1e5bff` (DC-15 family), `#2f7dff` (Z-6) | deep Republic blue bolts |
| Republic Navy | `#5b7fb8` | as GAR | uniforms `NavyUniform` 121 |
| Jedi Order | `#7fc4ff` | n/a | robes `Robe*` 59/60/112-119; hilts `SaberHilt` 120 |
| CIS | `#c8742a` | `#ff3b2f` | droid tan/brown; red bolts only for CIS |

Rule: blue bolts are friendly, red bolts are enemy. Nothing else on screen may be bolt-red or bolt-blue at bolt
brightness (hit flashes use a darker, desaturated red, see Feedback below).

### Jedi

Blade colours are `Jedi/SaberDesign.cs` crystals: blue `(0.22, 0.5, 1)`, green `(0.2, 1, 0.3)`, yellow `(1, 0.82, 0.18)`,
violet `(0.52, 0.26, 1)`, cyan `(0.15, 0.92, 1)`, amber `(1, 0.48, 0.08)`, white `(0.92, 0.94, 1)`. No red crystal for
players (that's Sith). Jedi UI uses the side tint `#7fc4ff` as its accent, otherwise the standard UI palette.

### UI

One family, the **holo** set (the character screen, Escape menu, HUD, comlink, mission board already use it):

| Token (`_feel.scss`) | Value | Use |
|---|---|---|
| `$feel-holo` | `#6fd3ff` | primary accent: titles, selection, focus, progress |
| `$feel-holo-dim` | `rgba(111,211,255,0.35)` | borders, inactive accents |
| `$feel-holo-faint` | `rgba(111,211,255,0.12)` | fills, hover wash |
| `$feel-text` | `#e6f2fb` | body text |
| `$feel-muted` | `rgba(230,242,251,0.55)` | secondary text, hints |
| `$feel-panel` | `rgba(8,16,28,0.9)` | panel background |
| `$feel-panel-deep` | `rgba(3,6,12,0.94)` | modal windows |
| `$feel-amber` | `#ffb547` | warning, attention, held item |
| `$feel-red` | `#ff4a3d` | danger, deny, damage numbers |
| `$feel-green` | `#7dffa1` | ok, online, healed |
| `$feel-staff` | `#ff7a2f` | staff and Game Master tools only (so staff UI is never mistaken for game UI) |

Feedback colours (screen effects, `Code/Feel`): hit edge `rgba(200,20,16,0.75)`, stun edge `rgba(150,205,255,0.6)`,
low-health edge `rgba(120,0,0,0.8)`, fades black, hyperspace flash `(0.85, 0.93, 1)`.

---

## 2. Models and materials (HD, since 2026-10-09)

Everything rendered is a PBR model now; the voxel palette and its size rules are retired with the voxel game.

| Asset class | Source | Rules |
|---|---|---|
| Ship | the Notorious port (`MapSource/notorious_build.py`), corridors from the Venator corridor kit (`notorious_corridors.py`) | the map's own Garry's Mod textures; corridors in the kit's PBR set; never mix the two inside one corridor |
| Troopers, Jedi | Battlefront II (`ModelSource/bf2_clones.py`), one Citizen rig | 72 units tall (1.83 m); battalion paint only through the material groups (`bf2_markings.py`), never a tint |
| Blasters, hilts, BF2 items | `ModelSource/bf2_props.py` | built in the grip frame, BF2 textures |
| Small original props | `ModelSource/hd_props.py` | flat PBR materials in `materials/props` |
| Craft | `ModelSource/bf2_craft.py` | true size (LAAT/i 713 units long) |

## 3. Exposure and brightness per area

Measured as the **mean luma of a play-mode screenshot** (0-255, HUD hidden with F1, standing still, looking along the
room at eye height; the editor bridge's `camera_screenshot` is fine), plus the share of clipped (255) pixels. The
lighting thread owns the values that achieve these (`Code/Lighting`, `LightZones.Defaults`, scene tonemapping);
**this guide only sets the targets**.

Baseline the targets assume (the lighting thread's two global knobs; tune these first, zones second):

| Convar | Baseline range | What it does |
|---|---|---|
| `swrp_light_exposure_floor` | **2.0-3.0** (default 2.4) | lowest auto-exposure the camera may drop to, so bright screens and viewports don't plunge a room into black |
| `swrp_light_ambient_scale` | **1.0-1.6** (default 1.25) | ambient fill everywhere, on top of the zones |

Inside those: scene tonemapping 1.6-3.5 auto-exposure aboard (`notorious_scene.py`), 0.5-1.1 outside the hull
(`SwrpHullLights`); a dark zone may cap exposure to x0.75-0.8 (and the floor drops with it) but never below x0.7
(`swrp_light_min_exposure`), and its ambient never below 35 % (`swrp_light_min_ambient`). If an area misses its target
with the knobs at baseline, fix the area's zone (`LightZones.Defaults`: Ambient / Exposure / Lamps), not the knobs.
How to read the column below: "zone" is the `LightZones` entry the area should use at the baseline knobs.

| Area | Target mean luma | Max clipped | Grade (`SwrpPostFx.Grades`) | Zone at baseline / notes |
|---|---|---|---|---|
| Corridors / general interior | 85-110 | 1 % | corridor, 6900 K | "Ship": ambient 0.8, exposure 1 |
| Main hangar | 105-130 | 2 % | hangar, 5500 K warm | "Main hangar": ambient 1.0; brightest interior |
| Bridges | 80-100 | 2 % (viewports) | bridge, 8200 K cool | "Port/Starboard bridge": ambient 0.9, haze 0.1; planet/space through the windows must not blow out (the exposure floor is what keeps the room readable here) |
| Medbay | 110-130 | 1 % | medbay teal, 7400 K | "Medbay": ambient 0.95; clean and bright |
| Engineering, centrifuge | 50-75 | 1 % | engineering, 5800 K | "Engine room"/"Centrifuge room": ambient 0.45, exposure 0.8, lamps 0.7-0.75, haze 0.25-0.3 |
| Brig | 45-70 | 1 % | brig red, 6300 K | "Brig": ambient 0.4, exposure 0.75, lamps 0.6, haze 0.35; darkest room aboard, bolts and sabers should light it |
| Character screen hangar | 70-95 | 1 % | none (6500 K) | the trooper under the spotlight is the brightest thing; face 140-180 |
| Space / outside the hull | 25-45 | 0.5 % (hull LEDs only) | n/a | stars and hull lights the only highlights |
| Hyperspace | 90-130 | flashes may clip | n/a | jump/exit flashes allowed to whiteout for under 1 s |
| Tatooine | 130-160 | 3 % | tatooine, 5600 K (sun 2.6, warm) | shadows not below 40 luma |
| Kamino | 75-100 | 1 % | kamino, 7600 K (overcast, sun 0.9) | lightning flashes up to 180 for under 0.3 s |
| Hoth | 140-170 | 5 % (snow) | hoth, 8000 K (sun 1.7, cold) | troopers' white armour must still separate from snow (rim light or shadow) |

Planets use their own grade in `SwrpPostFx.Grades` (`tatooine`, `kamino`, `hoth`), chosen when the camera is on the planet.

---

## 4. UI type scale

Two families only: **Orbitron** for headings, labels, buttons, numbers and HUD readouts; **Poppins** for chat,
descriptions and anything longer than a line.

| Token | Size | Use |
|---|---|---|
| `$feel-size-xs` | 9 px | micro labels, keycaps, units (letter-spacing 2 px, uppercase) |
| `$feel-size-s` | 11 px | body text, list rows, buttons |
| `$feel-size-m` | 13 px | emphasized body, panel sub-headings |
| `$feel-size-l` | 16 px | window titles (letter-spacing 4-5 px) |
| `$feel-size-xl` | 22 px | screen titles, big readouts (ammo, health) |
| `$feel-size-hero` | 48 px | the character screen name, splash titles |

Letter-spacing: 1 px body caps, 2 px labels, 3-5 px titles. Weights: 400 body, 700 titles and buttons.

## 5. Spacing and shape

4 px grid: `$feel-space-1` 4, `-2` 8, `-3` 12, `-4` 16, `-6` 24, `-8` 32. Window padding 16 x 20 (`16px 20px`), row
padding 6 x 10, gaps 8. Radius: `$feel-radius` 3 px for buttons, rows and chips; `$feel-radius-panel` 5 px for windows
and holo panels; fully round only for dots, avatars and the emote wheel. Windows get a 1 px `$feel-holo-dim` border;
console menus (lift, brig, ship systems, GM, battalion, build) also get a 3 px accent top border.

## 6. Motion

| Token | C# (`FeelMotion`) | Time | Easing | Use |
|---|---|---|---|---|
| `$feel-fast` | `Fast` | 0.12 s | ease-out in, ease-in out | hover, press, tooltips, small pop-ups, every outro |
| `$feel-med` | `Med` | 0.25 s | ease-out | backdrops, console windows, list items, side panels |
| `$feel-slow` | `Slow` | 0.35 s | ease-out | big holo panels, full-screen menus, fade to black |
| | `Reveal` | 0.8 s | in-out cubic | fading back in after a cut (deploy, landing ride) |

Mixins (`_feel.scss`): `feel-panel-in` (fade + rise 12 px, med), `feel-pop-in` (fade + scale from 0.97, fast),
`feel-holo-in` (rise 40 px + scale 0.92, slow), `feel-fade-in` (opacity, med), `feel-button` (hover wash + UI sounds).
Outros are always faster than intros. Nothing in the UI moves for more than 0.35 s except the screen reveal and looping
ambient motion (scanlines, pulses), which must be slower than 2 s per cycle. Progress bars animate width linearly at
0.08-0.15 s.

Camera and screen: hit flinch springs back within 0.4 s; hit edge flash 0.45 s; heavy-hit blur 0.4 s; deploy fly-in
2.4 s (smootherstep); death drift 7 s.

## 7. Sound

All audio is original and synthesized (`SoundSource/*.py`); never sample a game or film. One sound per meaning:

| Meaning | Sound | Where |
|---|---|---|
| hover / click / confirm / deny | `ui_hover`, `ui_click`, `ui_confirm`, `ui_deny` | `SwrpAudio`, scss `sound-in` |
| panel open / close | `ui_open`, `ui_close` | `SwrpAudio.Open/Close` |
| screen transition | `feel/transition_whoosh` | `FeelTransition.Deploy` |
| footsteps | ship: `ambient/step_grate`, `step_plate`; planets: `feel/step_sand`, `step_snow`, `step_wet` | `SwrpFootsteps` + `FeelSounds.Step` |
| bolt impacts | `ui/blaster_impact` + surface layer `feel/impact_metal`, `impact_sand`, `impact_snow` | `SwrpFx.Impact` + `FeelSounds.Impact` |
| voxel damage | `feel/impact_debris` (chips, small landings), `feel/debris_collapse` (big pieces) | `VoxelFx` + `FeelSounds` |
| near miss | `feel/bolt_whizz` | `FeelSounds` (polls bolts) |
| your hits | `ui/hit_marker`, `ui/hit_marker_kill` | `SwrpBlaster` |
| hurt / heavy hit | `ui/hurt`, `feel/hit_thump` (30+ damage), `ui/ear_ring` | `SwrpVitals`, `FeelHits`, `SwrpShake` |
| low health | `ui/heartbeat` (0.9 s, 0.65 s when at 15 or downed) | `SwrpVitals` |
| doors / lifts | `ui/door_open`, `door_close` + `feel/door_whoosh`; `ui/lift_ride` + `feel/lift_whoosh` | `SwrpDoor`, `SwrpTurbolift` + `FeelSounds` |

Levels: UI sounds 0.3-0.6 volume, world one-shots 0.4-0.6, big events (blasts, collapse) 0.8. Menu music 0.5 default.

---

## Outliers found (and who should fix them)

1. **Two UI colour families.** Holo (`#6fd3ff` / text `#e6f2fb` / red `#ff4a3d`): CharacterMenu, EscapeMenu, SwrpHud,
   ComlinkPanel, EmoteWheel, MissionBoard, RoleplayHud. "Accent" (`#5fb4ff` / text `#dfe8f2`): BattalionMenu, BuildMenu,
   LiftMenu, ShipConsoleMenu, InventoryPanel, BrigMenu (red `#ff5a4d`). CharacterMenu's red is a third value `#e0473f`.
   Rule: holo set everywhere; GM keeps `#ff7a2f`. **Done 2026-10-09 for the six console menus** (Lift, Brig,
   ShipConsole, Gm, Battalion, Build now use `$feel-*`). Still to do: InventoryPanel (`#5fb4ff`, held `#f2cf5b`) and
   CharacterMenu's `#e0473f` red (gamemode UI thread).
2. **23 font sizes** (7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 20, 22, 24, 26, 28, 30, 34, 40, 48, 50, 52 px).
   Map them onto 9/11/13/16/22/48: 7-8 → 9, 10 → 11, 12 → 13, 14-15 → 13 or 16, 17-20 → 16 or 22, 24-34 → 22,
   40-52 → 48. (gamemode UI, Jedi and Flight HUD owners)
3. **Six panel backgrounds** (`rgba(10,16,26,.95)`, `(14,8,10,.96)`, `(14,10,8,.96)`, `(8,22,38,.82)`, `(6,14,26,.9)`,
   `(10,16,26,.92)`). Use `$feel-panel` / `$feel-panel-deep`; tint only the top border for the brig (red) and GM (orange).
4. **Radii 1-6 px** mixed. 3 px controls, 5 px windows.
5. **Transition times 0.03-0.6 s** (15 distinct). Use 0.12 / 0.25 / 0.35; the HUD's 0.6 s opacity fade is a fading-HUD
   rule (keep, document it as the one exception) and 0.03 s should be 0.08-0.12.
6. **Console menus had no intro/outro** (lift, brig, ship systems, GM, battalion, build): they popped. Now on
   `feel-panel-in` (two-line hooks, see the README's Feel section).
7. **Battalion UI tint vs armour paint differ** (e.g. 501st `#2f6fdb` vs `#345ca8`). Intended; now a written rule (UI tint
   in 2D, palette in 3D) rather than an accident. The 501st UI tint drifted most; **moved to `#3a66c4` on 2026-10-09**
   (static registry: takes effect after an editor restart).
8. **Droid voxels (0.9) are coarser than clones (0.55)**, so droids look blockier next to troopers. Either intended
   (machines read chunky) or bring them to 0.55-0.6. **Angelo chose to match (2026-10-09)**: `VoxelDroideka.Size`
   0.9 → 0.55 in `Code/Voxel/VoxelDroid.cs`, handed to the Characters/NPC thread.
9. **Planets have no area grade**: on Tatooine/Kamino/Hoth `SwrpPostFx` uses the corridor grade. Add `tatooine`
   (warm, 5600 K), `kamino` (cool, 7600 K, low saturation) and `hoth` (cold, 8000 K) grades. **Done 2026-10-09** in
   `SwrpPostFx.Grades`, picked when the camera is on a planet (static dictionary: editor restart to take effect).
10. **Saber hilt voxels (0.16) use their own palette** (`materials/jedi`), the only model outside the shared palette.
    Fine as an exception, listed so nobody copies it.
11. **Landing and hyperspace fades live in their own code** (`SwrpPlanets.Fade` 0.8 s black, `SwrpSpace.Flash` white).
    Their timings already match `Reveal`; `FeelTransition.Landing()/Hyperspace()` exist if the owners want one path.
