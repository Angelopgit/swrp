# SWRP asset licence audit (2026-10-21)

Nothing was removed, moved or published. This is a read-only survey of `Assets/` (4.8 GB on disk, 2.0 GB of it compiled `_c`
files, which is roughly what a published build would carry). Sizes below are compiled MB.

## Verdict

About **78 % of the compiled game (1,575 of 2,013 MB) is ripped content that can't be published.** Everything that makes it look
like Star Wars today (troopers, guns, craft, the Venator map) is in that 78 %. The original 22 % is mostly sky textures, the
Tatooine terrain/town, sounds, and a few props and droids.

Separately from the rips: the game is a Star Wars fan project. Even with 100 % original assets, Lucasfilm's names and designs
are their IP. Fan games are usually tolerated when free and non-commercial, but that is a risk you choose, not a licence.

## Must not ship (local rips)

| What | Where | MB | Already has an original fallback? |
|---|---|---:|---|
| BF2 clone troopers, armour parts, commanders' base, navy crewman (BF2 clone recoloured) | `models/troopers` (BF2 parts), `models/clones`, `models/crew/crewman_navy` | 497 | No (Citizen only) |
| Notorious VMF map, its GMod textures, scene and door/prop/holo data; Venator corridor kit baked in (zip has no licence file) | `models/notorious`, `materials/gmod`, `materials/notorious`, `scenes/notorious.scene`, `data/notorious_*.json` | 311 | No |
| GMod HL2MP animation sets (Valve) | `models/troopers/*_gmod_*`, `*_patrol`, `*_slowwalk`, `data/gmod_aim.json` | 195 | Yes: the older `clone_animationlist` on s&box's own human clips |
| BF2 blasters | `models/weapons/bf2_*` | 166 | No |
| GC/Squad: B1/B2 droids, Rex/Wolffe, Squad soldier anims and prone | `models/troopers/gc_*`, `*_squad`, `squad_clone_*`, `gc_anims` | 147 | Partly: original `models/droids/droid_b1/b2/commander` |
| BF2 LAAT, ARC-170, V-wing | `models/craft` | 117 | No (original `models/space` ships are background-only) |
| TFA and GC blasters | `models/weapons/tfa_*`, `gc_*` | 92 | No |
| BF2 hilts, droideka, R2-D2/R5-D4, thermal detonator, bacta canister, stun baton | `models/jedi`, `models/droids/bf2_*`, `models/crew/bf2_*`, `models/props` (3 items), `data/droideka_poses.json` | 40 | Astromech: yes (`models/crew/astromech_*`). Others no |
| TFA weapon sounds | `sounds/weapons_ext` | 3 | **Yes**, synthesized `sounds/ui/blaster_*` kick in when the folder is gone |
| GC saber sounds | `sounds/jedi_ext` | 0.3 | **Yes**, `sounds/jedi` |
| gWeather wind | `sounds/tatooine/ext` | 2.5 | **Yes**, `sounds/tatooine` synth |
| BF2 droid voices | `sounds/npc_vo` | 3.4 | Droids just go quiet |
| BF2 LAAT engine sounds, BF2 missile explosions | `sounds/flight/laat`, `sounds/flight/bf2` | 0.5 | **No, code plays them by path**: needs a two-line swap to `sounds/flight/engine_gunship` / `explode_*` |

The five sound folders are the easy part: deleting them from a release copy costs nothing except the LAAT and missile paths.

## Safe to ship

- Synthesized audio: `sounds/ui`, `ambient`, `feel`, `jedi`, `space`, `flight` (top level), `tatooine` (top level), `music` incl. club tracks. Kenney UI clicks are CC0.
- Orbitron font (OFL, licence file is next to it).
- Generated skies `materials/space` (358 MB, see download size), Tatooine terrain, rocks and town, medbay/hangar/armory/space-ship models, original droids B1/B2/commander, astromech and mouse droid, 9 of 12 item props (`hd_props.py`).
- Facepunch Sandbox build-tool code (MIT, licence kept in `Code/Build`).
- sbox.game package references (`haluca.monobdwhite`, `haluca.monopticonhead`, `bdne.*` crates) load from sbox.game itself, so they are fine as references.

## Proposed replacements (each needs your OK)

1. **Troopers (the big one).** Recommended: look on sbox.game for a community clone model whose author allows use, and fall back to the Citizen with original armour pieces built like the droids. Building a full original clone set is a large art job.
2. **Map.** Recommended: ask the Notorious authors (Bananakin, Goldie, Halves, 122jimbo, Lord Trilobite, Grady, Bishop) for written permission and the origin of the GMod texture packs. If any says no, the alternative is an original Venator interior built from our own kit, which is a large job. The corridor kit's source needs identifying too.
3. **Animations.** Recommended: ship the `clone_animationlist` set on s&box's own human clips (already in the project) and keep GMod/Squad sets local.
4. **Weapons, craft, hilts, 3 items, droideka.** Recommended: original low-poly models in the style of `hd_props.py`, one batch per category.
5. **Sounds.** Recommended: drop all five rip folders from the release and repoint LAAT engine and missile blasts to the synthesized set.

## How to keep the rips out of a release

The sound code already checks "is the local folder present, otherwise use ours". Recommended: extend that rule to models
(class, weapon, craft and droid paths fall back to the original set when the rip is missing), then build releases from a copy
with the rip folders removed. That keeps your local copy exactly as it is today.
