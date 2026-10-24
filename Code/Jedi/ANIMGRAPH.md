# Lightsaber animgraph: parameters and state machine

The saber runs today on a **procedural layer** (`SaberMotion` + `SaberRig`) over the clone/human animgraph: the graph keeps
playing locomotion (holdtype 0), and the rig takes the arms, spine and head (ProceduralBone) to put the hands on the hilt
and drive the cuts. This file is the spec for an **authored** saber graph to replace or sit under that layer. Every
parameter below is already written to the body each frame by `JediCombat.DriveRig`, so a graph that reads them works
without code changes. When an authored clip set exists, lower the rig's arm/spine weights (they blend from the animated
pose) instead of removing it: the procedural layer still supplies off-hand IK, hand-to-hilt attachment, look-at and
hit reactions.

## Parameters

| Name | Type | Meaning |
|---|---|---|
| `move_speed`, `move_x`, `move_y`, `wish_*`, `b_grounded`, `duck` | (stock) | set by the PlayerController as now |
| `saber_active` | bool | blade lit (or thrown) |
| `is_blocking` | bool | guard held (owner-predicted, then host) |
| `attack_index` | int | combo step of the current swing, -1 when not swinging |
| `swing_dir` | int | `SwingDir`: 0 none, 1 Left (right→left), 2 Right (left→right), 3 Overhead, 4 Thrust, 5 Spin, 6 Rising |
| `form_id` | int | canon form number - 1: 0 Shii-Cho, 1 Makashi, 2 Soresu, 3 Ataru, 4 Djem So, 6 Juyo |
| `saber_mode` | int | 0 single, 1 dual (shoto in the left hand), 2 saberstaff |
| `saber_react` | int | 0 none, else `ReactKind` + 1: 1 Blocked recoil, 2 Parried, 3 Guard break, 4 Lock lost, 5 Knocked, 6 Block shake |
| `saber_lock` | bool | in a blade lock |
| `force_cast` | int | 0 none, else Force power + 1: 1 Push, 2 Pull, 3 Leap, 4 Speed, 5 Heal, 6 Lightning |

Animation set names per form (`SaberForm.AnimationSet`): `saber_shiicho`, `saber_makashi`, `saber_soresu`, `saber_ataru`,
`saber_djemso`, `saber_juyo`. Clips inside a set: `idle`, `walk`, `run`, `block_mid`, `block_high`, `parry`,
`atk_<dir>_<step>` (e.g. `atk_left_0`), `atk_<dir>_heavy`, `react_blocked`, `react_parried`, `react_guardbreak`, `lock`,
`lock_win`, `lock_lose`, `throw`, `catch`.

## State machine

```
Locomotion (holdtype 0) ──saber_active──▶ Stance Idle [form blend: form_id]
                          ◀─!saber_active─  (0.25 s)

Stance Idle ──swing_dir≠0──▶ Attack [select atk_<swing_dir>_<attack_index>, heavy when attack_index uses the heavy clip]
            ──is_blocking──▶ Block [block_high when looking up, else block_mid]          (0.08 s in, 0.15 s out)
            ──saber_react≠0─▶ Stagger [react_*]                                           (0.05 s)
            ──saber_lock───▶ Lock                                                         (0.1 s)
            ──force_cast≠0─▶ (additive left-arm layer: cast_<power>)                      (0.1 s)

Attack ──clip end──▶ Stance Idle                (recover blend 0.2 s)
       ──swing_dir changes (new serial)──▶ Attack (cross-fade 0.06 s from the current pose: chains)
       ──saber_react≠0──▶ Stagger               (0.04 s: a parry interrupts the cut)
Block  ──!is_blocking──▶ Stance Idle            (0.15 s)
       ──saber_react=6──▶ Block (additive shake) ;  =3 ──▶ Stagger (guard break)
Stagger ──clip end──▶ Stance Idle               (0.2 s)
Lock ──!saber_lock──▶ Stance Idle or Stagger (lock_lose when saber_react=4)
any ──dead/downed──▶ the existing death / downed states
```

Layers: lower body always locomotion (masked at `pelvis`), upper body the saber state machine above, an additive layer for
`force_cast` on the left arm, and the procedural layer (`SaberRig`) last.

## Timing the clips must match

The host times hits from `SaberMotion.TimingFor` (seconds at speed 1, divided by form tempo × combo step speed × crystal):
light swing windup 0.12, strike 0.20, follow-through 0.12, recover 0.24; heavy 0.20 / 0.27 / 0.16 / 0.34; spin strike ×2.1,
thrust strike ×0.75. Hits count during the strike and the first third of the follow-through, so author the contact
frames inside that window, and play clips at `SwingSpeed` (synced on JediCombat).
