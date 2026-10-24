# Lightsaber refinement passes

The `/loop 30m` refinement pass takes the first unchecked item each run. A run is skipped if Angelo is in play mode
or another thread is testing in the editor. Testing uses `swrp_saber_test` against clone and droid NPCs, never Cub or
Angelo's Jedi. FPS is measured on the bridge with several sabers lit: it must stay at 100+ with no memory climb.

## Queue (round 1)

- [~] Swing feel: anticipation, follow-through, hit-stop, camera punch (hit detection fixed; feel tuning waits for a healthy frame rate)
- [~] Blocking and perfect parry timing (anti-spam + HUD flash in; needs a play test at a healthy frame rate)
- [~] Saber clashes and locks (stuck-lock fixes + contact light in; needs a play test)
- [~] Bolt deflection (focus limit + flick/flare in; needs a play test)
- [~] Saber throw and recall (spin-loop fix + Doppler in; needs a play test)
- [~] Each form's tempo and combos (chain sync fix + Ataru hop in; needs a play test)
- [~] Force power visuals and sound (crackle throttle + knock jolt in; needs a play test)
- [~] Blade trails, glow and burn marks (trail fix/smoothing + ignition tip in; needs a play test)
- [~] The saber builder in character creation (rebuild for existing Jedi added; needs a play test)
- [~] First-person view (view-model layout + sway in; needs a play test)
- [~] NPC reactions to saber hits (stagger, droid cut-down, burn fallback in; needs a play test)

## Log

- 2026-10-07 17:10 run 1: skipped (Angelo in play mode as Cub).
- 2026-10-07 17:45 run 2: skipped (play mode still running as Cub; editor at ~9 fps).
- 2026-10-07 18:15 run 3: skipped (play mode still running as Cub, ~8 fps, no activity since 17:09).
- 2026-10-07 18:45 run 4: skipped (play mode still running as Cub, idle since 17:09).
- 2026-10-07 19:15 run 5: skipped (play mode still running as Cub, idle since 17:09).
- 2026-10-07 19:31 run 6 (swing feel): Angelo let me stop play mode. Test stand + `swrp_saber_targets` (5 posts in a fan).
  Found: the hit window was checked once per frame, so at low fps (the editor ran 4-12 fps: VRAM 11.3/12.3 GB, GPU 99%)
  a strike could fall between two frames and miss (thrust and spin missed 100%, rising 80%), and wide cuts slid their
  ends straight between frames, cutting the corner. Fixed with SaberSweep.Step: the host walks the hit window in 1/90 s
  slices of swing time (SaberMotion.Probe) and turns the blade between slices (slerp), plus an emitter-to-tip trace for
  anything the blade starts inside. After: left/right/rising/spin 5/5 posts, thrust and overhead hit the post in line.
  Polish: the blade flares white-hot for 0.14 s where it bites (hit, block, clash), lighting the room brighter.
  FPS: not measurable against the 100 fps rule until the editor restarts (baseline 11 fps before any saber).
  Still to do for this item: anticipation/follow-through timing and camera punch at a real frame rate.
- 2026-10-07 20:07 run 7 (blocking and parry): editor still at ~5 fps with the GPU at 100% outside play (restart pending),
  so this run was a code pass, not a play test. Found: tapping guard up and down restarted the parry window every time,
  so spamming mouse 2 made every block (and every bolt deflection) perfect. Fixed: a guard raised within 0.45 s of
  dropping it gets no parry window. Polish: PERFECT PARRY (defender, white) and PARRIED (attacker, amber) flash on the HUD.
- 2026-10-07 20:37 run 8 (clashes and locks): still a code pass (editor ~5 fps, GPU 100%, restart pending). Found: only
  the lower-id Jedi resolves a lock, and a Jedi who stopped being able to wield (dropped the saber item) skipped the host
  tick entirely, so the partner stayed locked at 15% speed forever. Fixed: losing the saber ends the lock, and the other
  side takes over resolving after 4.5 s. Polish: a flickering hot-white light where the blades grind, lighting both duellists.
- 2026-10-07 21:07 run 9 (bolt deflection): code pass (editor ~5 fps, restart pending). Found: deflecting never checked
  focus, so a guarding Jedi at 0 focus still turned every bolt. Fixed: each deflection needs its focus cost; at zero the
  bolt gets through and a raised guard breaks. Polish: every deflection snaps the blade to meet the bolt and flares it.
- 2026-10-07 21:37 run 10 (throw and recall): code pass (editor ~5 fps, restart pending). Found: the spinning-throw loop
  (1.2 s) was restarted on the hum's 2.4 s timer, so a flying saber went silent half the time, and the hum kept its old
  loop for up to 2 s after a throw or a catch. Fixed: own loop length, and the loop switches the moment the saber leaves or
  returns. Polish: Doppler on every blade's hum: one coming at you rises in pitch, one passing away drops.
- 2026-10-07 22:07 run 11 (forms and combos): code pass (editor ~5 fps, restart pending). Found: the owner picks the
  combo step at the button press but a heavy is only sent on release, so the host (checking its chain window at receive
  time) reset held-heavy chains to step 0 while the owner kept playing step N: different arc, direction (a finisher's spin)
  and damage on each side. Fixed: the host judges the chain by time since the last swing started (length + window + max
  charge + lag), a hit reaction breaks the chain, and when the host picks a different step it sends a serial the owner
  didn't predict, so the owner replays the host's swing. Polish: Ataru's spinning finisher leaves the ground in a short hop.
- 2026-10-07 22:37 run 12 (Force visuals and sound): code pass (editor ~5 fps, restart pending). Found: Force Lightning
  played its 0.4 s crackle on every 0.15 s channel tick, stacking three copies into a phasing roar. Fixed: one crackle at
  a time per caster. Polish: anyone thrown by Push, Pull or a lost lock gets a view jolt sized to the throw.
- 2026-10-07 23:07 run 13 (trails, glow, burns): code pass (editor ~5 fps, restart pending). Found: the trail kept only
  0.085 s of samples, so below ~25 fps it had one sample and vanished, and its points were joined straight (a fan of
  cuts). Fixed: never fewer than three samples (age scales with frame time), rounded through Catmull-Rom (3 points per
  pair). Checked burns and body burns: capped (240) or fading (24 s), so no memory climb. Polish: the tip leads the blade
  out of the emitter as a hot point when it ignites and retracts.
- 2026-10-07 23:37 run 14 (saber builder): code pass (editor ~5 fps, restart pending). Checked: the builder sits in the
  scrolling right panel, its rows use the menu's styles, the hangar mannequin follows it. Found: a saber could only be
  built at enlistment, so existing Jedi (Angelo's) were stuck with the default blue. Fixed: the roster dossier of a Jedi
  shows the same builder (SaberEditor), live on the mannequin, with SAVE LIGHTSABER (JediNet.RequestSaber: the host takes
  only the saber keys, saves, and updates a deployed character at once). Hooks: one line each in CharacterMenu.razor.
- 2026-10-08 00:07 run 15 (first person): code pass (editor ~5 fps, restart pending). Found: first person drew the saber
  on the body's (lagging) yaw with only half the view's pitch round the chest, so it drifted sideways on turns and slid
  off the bottom of the screen when looking down; and the shadow body's arms were stretched toward it. Fixed: in first
  person the motion is sampled flat and laid out round the eyes (a view model: same place on screen wherever you look,
  nudged up into view), and the shadow body's arms are left to the animation. Polish: the saber trails a head turn
  slightly (view lag) so it has weight.
- 2026-10-08 00:37 run 16 (NPC reactions): code pass (editor ~5 fps, restart pending). Found: NPCs took saber damage with
  no reaction at all (kept aiming and firing through the blow), and burn marks needed bone objects, so a body without
  them (non-voxel NPCs) got none. Fixed: a blow that doesn't kill shoves the NPC a step along the cut and staggers it out
  of its aim (0.3 s light, 0.7 s heavy; shielded droids are left alone); body burns fall back to the body itself.
  Polish: a droid cut down throws a shower of sparks from the cut, a molten glow, a pop of smoke and a jolt.

## Round 1 done (all items code-passed). Round 2 starts from the top once the editor is restarted: everything needs a
## play test at a real frame rate, the fps rule measured, and the swing-feel timing tuned.
- 2026-10-08 00:45 (local 20:45) run 17: skipped. The editor was restarted (GPU 25%, 8.2 GB) and someone is in play mode.

## Round 2

- 2026-10-07 21:45-22:00 (stances and animation, Angelo's ask): built `swrp_saber_stage` (hidden row of Jedi, one per
  form, frozen in a pose, own camera) and `swrp_saber_forms` (prints loaded form data). Found: every form stood the same
  (straight legs, square hips; only the blade angle differed) and every cut had the same shape. Added: per-form footing
  with leg IK (feet width/depth, side-on hip turn, crouch, weight shift; feet stay planted through cuts; gives way to the
  walk cycle when moving), per-form cut shape (CutTilt: flat Makashi to steep Djem So chops; WristLead: wrist-driven
  Makashi; BodyDrive: hips and chest turn into a cut, most for Ataru/Juyo/Djem So), Makashi's off hand raised behind,
  Niman (form VI), per-form Reach and ForceCost. Found too: the editor kept form assets loaded before new fields existed,
  so SaberForm.Version/DataVersion now makes stale assets fall back to the built-in table (rebuilt on hotload via
  BuiltInRevision). FPS: 7 sabers lit in the main camera's view held the 90 fps cap, same as with none.
  Next: overhead hands sit in front of the face (raise them above the head), more twist mid-cut, check walking with a
  saber lit (stance blend), dual and staff stances.
- 2026-10-07 22:57 (retry of the 22:37 run; 22:07, 22:22 and 22:28 found play mode): on the pose stage. Overhead hands now
  rise above the head in the wind-up (were in front of the face); the body leads the cut (twist sampled 0.18 ahead).
  Found: dual's reverse-grip shoto ran straight down through the thigh at guard (now angled out and back, clear of the
  leg); trails drawn as 4 separate strands read as a comb of spikes on a fast cut (now 7 overlapping strands: a faint fill
  and a bright tip edge); a pose snap or teleport smeared the trail across the jump (now resets). Violet crystal toned
  bluer (read as pink). Staff guard is the same for every form (next). FPS: 7 dual-wielding Jedi (14 blades) in the main
  camera's view held the 120 fps cap (worst frame 12-14 ms vs 9-13 ms without), GPU memory flat at 10.77 GB.
- 2026-10-07 23:28 (retry of the 23:07 run): saberstaff. The staff guard now follows each form (height, tilt, turn off
  square). Found: staff swings turned the staff round the body, so the back blade swept through the wielder's own side.
  Fixed: a staff now spins like a wheel in front of the body, its plane tipped by the cut, riding out toward the side
  being cut. Hit test vs the 5-post fan: spin 5/5, left 4/5, right/rising 3/5, overhead/thrust the posts in line: a staff
  trades the single blade's wide side reach for two blades in front (by design for now). FPS: 7 staffs (14 blades) in
  view held the 120 fps cap. Next: per-form tempo feel in motion (a clip), block/parry poses per form.
- 2026-10-08 00:57 (retry of the 00:37 run; 23:37, 00:07, 00:28 found play mode): per-form block poses checked on the
  stage (Makashi's one-handed parry held out ahead, Soresu tight and upright, Djem So high and flat, Juyo low); no blade
  through a body. NPC hit test: spawned NPCs walk off at once, so the test stand now pins NPCs placed within reach (test
  only). Against a pinned B1 and a pinned clone, every swing (left, right, overhead, thrust, rising, spin) hit once, no
  misses (the host counts one hit per target per swing). GPU memory over 3 stage rebuilds: 10946 / 10950 / 10945 MiB
  (flat). FPS: 7 sabers in the main camera's view held 118-120 (cap 120), worst frame 14-23 ms.
- 2026-10-08 01:14 run (short: stopped before the flight loop's :17 slot): Djem So demo stand, green crystal, close up.
  The blade's light spills green over the wall and the body as intended; the stance reads. Found: the hands grip the hilt
  with the animation's open fingers (flat fingers along the hilt, not a fist). Next: curl the fingers round the hilt
  when gripping (ProceduralBone on finger_*), and catch the Force push frame on camera.
- 2026-10-08 02:28 (retry of the 02:07 run; 01:37, 01:57 found play mode): finger curl tuned close up on the stand's hands.
  Axes tried with swrp_saber_curl: 0 0 1 (hands behind the hilt), 0 -1 0 (fingers bent backwards), 1 0 0 (fingers twisted
  and spread), 0 0 -1 (fist beside the hilt), 0 1 0 (fingers wrap round the hilt): defaults now axis 0 1 0, 75 degrees a
  joint, thumb 32 (left hand mirrored). FPS: 7 sabers in view 120 (cap), worst frame 12-13 ms; GPU memory 10979 MiB.
  Force push frame still not caught on camera (next).
- 2026-10-08 02:57 (retry of the 02:37 run): Force visuals on the pose stage (it can now hold a power and repeat its
  effect: swrp_saber_stage push|pull|lightning|heal|leap). Found: Push threw big grey hoops far ahead of the hand;
  Lightning was a few thick white zigzags (neon, not lightning); Pull and Heal barely showed. Changed: Push is now 3
  faint pale-blue ripples close to the hand that swell and slow; Lightning is twice the segments with half the jag,
  thin blue-violet arcs with a white-hot main bolt; Pull streams brighter haze to the hand with a ring closing on the
  target; Heal motes bigger and brighter. FPS: 7 Jedi channelling lightning in view held 119-120 (cap), worst frame
  14-17 ms; GPU memory 11.1 GB. Pull still to frame properly (its source sits off-camera on the stage).
- 2026-10-08 03:14 run (short, out before the :17 flight slot): framed Force Pull wide. Only the ring closing on the
  target showed; the haze stream was too faint to read. Added a rippling pale thread from the target to the hand that
  thins and fades (FxThread), and bigger, brighter haze. Not yet seen on camera (next run).

## Round 2 status: forms/stances, cut shapes, blocks, grip, staff, dual, trails, Force visuals done (see above).
## Round 3 (from the top): swing feel in motion (a clip of each form), parry timing in a real duel, NPC reactions.
- 2026-10-08 03:57 (retry of the 03:37 run): Force Pull's new thread showed as a bright straight laser; softened to a
  pale, wavier, half-strength tug. Round 3 start, swing feel in motion (Juyo demo stand filmed from the side as a burst):
  legs and arms read, but the upper body stood bolt upright in every guard. Added StanceBend per form (Juyo hunched 14,
  Ataru 9, Djem So 5, Shii-Cho 3, Niman 1, Soresu -1, Makashi -4 upright back). FPS: 7 sabers in view 120 (cap).
- 2026-10-08 04:28 (retry of the 04:07 run): torso lean checked side-on, one form at a time (a row overlaps from the
  side): Juyo now a low forward lunge with the blade held low ahead; Makashi upright, blade extended one-handed, off
  hand raised behind. They read as different fighters. NPC reactions in motion can't be judged on the stand: its hit
  test only counts, the stagger/shove lives in the host's JediCombat.BodyHit (needs a real Jedi player, or a test hook
  that runs BodyHit for the stand). Next: that hook, then parry timing in a duel (two stands).
  FPS lesson: the first ~30 s after jumping the camera to the stage reads 40-80 fps (voxel streaming and the stage's
  bodies building); measure after it settles. Settled: 7 sabers in view 120 (cap), worst frame 12-15 ms, vs 9-10 ms with
  none; finger curl on or off made no difference. GPU memory 10.76 GB.
- 2026-10-08 04:57 (retry of the 04:37 run): the test stand now runs the host's NPC stagger on what it hits (shared
  JediCombat.NpcStagger: a shove along the cut and a short stun, no damage; the pin lets go 0.8 s). Filmed a pinned B1:
  the shove read (it was carried 7-16 units along each cut) but the body stayed bolt upright, so nothing looked hit.
  Added FxFlinch: on every machine, a body without a posing saber rig (troopers, droids, NPCs) snaps its spine and head
  away from the blow and settles back over 0.35 s (26 degrees over four bones); a Jedi's rig still takes its own flinch.
  FPS: 7 sabers in view 119-120 after settling. GPU memory 11.98 GB, up from 10.76 at 04:30: the editor has been
  through several play sessions since (runtime models are not freed between sessions, see the voxel notes); worth an
  editor restart when convenient.
- 2026-10-08 05:14 run (editor in play mode, so a desk check): parry fairness by the numbers (first combo step, speed 1
  crystal). Time from swing start to the blade reaching a foe in front:

  | form | light | heavy | parry window | focus/swing | swings per 100 focus | focus a blocked light costs (x defender BlockCost) |
  |---|---|---|---|---|---|---|
  | Shii-Cho | 220 ms | 394 ms | 160 ms | 8 | 12.5 | 14.0 |
  | Makashi | 179 ms | 320 ms | 220 ms | 6 | 16.7 | 12.2 |
  | Soresu | 229 ms | 411 ms | 180 ms | 7 | 14.3 | 11.3 |
  | Ataru | 167 ms | 299 ms | 140 ms | 11 | 9.1 | 12.0 |
  | Djem So | 263 ms | 471 ms | 150 ms | 10 | 10.0 | 19.2 |
  | Niman | 216 ms | 386 ms | 170 ms | 7 | 14.3 | 13.3 |
  | Juyo | 153 ms | 274 ms | 120 ms | 14 | 7.1 | 17.6 |

  Reading: light cuts land faster than anyone reacts (150-260 ms plus network lag), so lights are met by a guard already
  up, not parried on reaction; heavies (270-470 ms, with a visible wind-up) are the ones a parry answers. That is the
  intended shape. No change. Next: a two-stand duel harness to see blocks and parries play out.
- 2026-10-08 05:57 retry (duel stand, swrp_saber_duel djemso soresu, filmed on the pose stage's floor): first run only 3 of
  ~9 cuts met the guard. Causes and fixes:
  - the harness's contact test sampled one frame, so fast cuts jumped over the other blade: now swept from last frame's
    blade to this one (6 steps), and only inside the swing's hit window (it was catching wind-ups and recoveries);
  - side cuts passed under the form's slanted block and rising cuts under it entirely (closest 3.7-5.5 units): the block
    now meets the cut. SaberMotion.Block( on, from ) takes the incoming SwingDir: upright on the side a sweep comes from
    (hilt at the waist), a low bar across the hips for a rising cut, the forte across a thrust; overhead and spins keep the
    form's own block. Players get it too: JediCombat.IncomingCut() picks the nearest Jedi in reach swinging at you (as
    this client sees it) and the guard swings over to meet it, holding until the next cut. Host blade contact still uses
    the form block (blocks resolve on the body either way).
  - the log printed the guard time after resetting it (blocks read 1-3 ms); fixed, and it now prints when contact came
    vs the expected time, and NO CONTACT with the closest gap for any cut that never met.
  Result: all five directions meet, alternating PARRIED / blocked (parries 67-111 ms after the raise, contact within
  ~25 ms of the expected time). Sparks and the flare sit on the crossing point; the X of the blades reads at the clash.
  FPS: 9 sabers lit (stage row + duel), 6+ in view, 119.6-120 avg, worst frame 14-19 ms.
  Still to see: the rising swing's trail renders as a hard-edged wedge when seen edge-on; check trail fill there.
- 2026-10-08 06:07 run (late start 06:18): trails. Filmed the duel at quarter speed (new dev command swrp_saber_slowmo
  <scale>, sets the local scene's TimeScale). The trail's inner fill had a saw-tooth edge (square strand ends stacked
  under the blade, and the ribbons notching at their joints) and burned into a solid wedge when the cut was edge-on.
  Fixed in SaberVisual: fill fades with how squarely the swing's plane faces the camera (to 20% edge-on; the bright tip
  edge stays), strands taper at the newest end too, Catmull-Rom 5 points per pair instead of 3, inner fill alpha
  0.16 -> 0.12. Duel harness: the defender's cue now runs per frame on the scaled clock (it ran in fixed update with a
  real-time stopwatch, so slow motion dropped guards and failed parries).
  Stand vs a lone pinned B1 (juyo): every direction hits it, one counted hit per swing. A clone and a B1 together shoot
  each other, so test them one at a time.
  FPS: 7 sabers on the stage, 96-105 in the first ~30 s, then 113-119 (worst frame 15-17 ms). GPU memory: nvidia-smi
  now reports used > total (the counter wraps), i.e. VRAM is full after a long editor session; fps holds, but an editor
  restart is due.
- 2026-10-08 06:56 retry: filmed the trail fix. Duel cuts stop at the guard, so they barely trail; the free-swinging stand
  (swrp_saber_test 0 0 ataru, placed on the stage floor at -50,-120,-14001 facing -90, slowmo 0.2) shows it: the fill is
  a soft blue sheet now, the saw-tooth reduced to a faint ripple along the trailing edge. Good enough; revisit only if it
  reads at full speed. FPS: 8 sabers (stage row + stand) 120 avg, worst frame 11-16 ms; GPU memory 11.9 GB in use.
- 2026-10-08 07:07 run (07:14 start): NPC reactions on camera, side-on at one-fifth speed. Found: the test stand (and the
  duel stands) spawned at scale 1 while players and NPCs are scaled by PlayerScale (1.3 now), so the stand Jedi stood a
  head shorter than the clone and its reach was understated against NPCs. Fixed: swrp_saber_test and swrp_saber_duel
  build their Jedi at SwrpPlayer.PlayerScaleFor (duel gap scaled too). A pinned clone was then hit by every direction,
  one counted hit per swing. The flinch reads, but small from the side (head and chest tip away a few degrees); left as
  is. Note: swrp_saber_test -1 does not remove a stand left from an earlier hot-reload (two stands overlapped; disabled
  the stale one by hand). FPS: 7 sabers 120 avg, worst frame 14 ms; GPU memory 11.8 GB (flat).
- 2026-10-08 07:37 run (07:44 start): logic. (1) swrp_saber_test left old stands behind: a Jedi stand switches its
  mannequin off and SwrpAnimMannequin.Spawn only clears enabled ones; TestCommand now clears old "Anim Test" stands
  first (one stand after two calls; none after -1). (2) Filmed Makashi vs a pinned B1: after a run of cuts the droid
  stood inside the Jedi. NpcStagger shoved along the blade's motion, which on a back-sweep points at the attacker, so
  real NPCs could be dragged into the Jedi too. Now the shove is the cut blended with "away from the attacker" and never
  toward him. The stand's pin returns the NPC to its spot after the 0.8 s release instead of re-pinning where it landed.
  B1 then hit once by every direction, staying in front. FPS: 7 sabers 120 avg, worst frame 12-17 ms; GPU 11.7 GB flat.
- 2026-10-08 08:07 run (08:14 start): forms side by side (swrp_saber_forms table). Shii-Cho and Niman were near twins
  (tempo 1/1.02, damage 1/0.95, focus 8/7, block 1/0.9, parry .16/.17, same reach, near-identical three-step combos;
  only Niman's Force discount set them apart). Retuned (DataVersion 6, BuiltInRevision 8, assets regenerated):
  Shii-Cho = the widest, cheapest sweeps for crowds: focus 6 (cheapest of all), reach 1.06, arcs 175/185 then the spin,
  plainest parry (0.15) and a slightly costlier block (1.05). Niman = tight and Force-woven: arcs 120, a thrust in the
  middle of the chain (Any, Thrust, Rising), reach 0.95, damage 0.9, steadier block (0.85), parry 0.18, deflect 0.65,
  Force powers 30% cheaper, chain window 0.6 s (vs 0.45) to weave a power between cuts.
  Stand: Niman and Djem So each hit a pinned clone once per cut. FPS: 7 sabers 120 avg, worst frame 12-13 ms. GPU
  memory counter wrapped again (used > total), so VRAM is at the limit; fps unaffected so far.
  Next: the trail is still drawn as 7 camera-facing strands; a fast sweep near the camera shows them as a stair-stepped
  fan. Build it as one swept ribbon (a strip between the blade's base-ward and tip curves) instead.
- 2026-10-08 08:37 run (08:44 start): trail rebuilt as one swept sheet (new Code/Jedi/SaberTrailSheet.cs): a
  SceneCustomObject per blade draws a triangle strip between the smoothed curves the blade traced at 28% and at the tip
  (Graphics.Draw, materials/tools/vertex_color_translucent.vmat with the D_BLEND additive combo; that shader ignores
  vertex alpha, so the fade is carried in the colour). Fades in from the oldest end, inner edge at a quarter of the
  tip's strength, both windings. The six inner LineRenderer strands are gone; the bright tip strand stays (TrailAt =
  { 1 }). First try drew an opaque blue wedge (alpha ignored); premultiplying fixed it. Dev filming aid:
  swrp_saber_strikeslow 1 (the test stand drops the scene to 3% speed for 3 s each time a cut reaches its hit window).
  Filmed Ataru at the strike: one smooth pale smear behind the blade, no stair steps. FPS: 8 sabers (stage row + the
  swinging stand) 120 avg, worst frame 10-14 ms; GPU memory 11.9 GB; no console errors.
- 2026-10-08 09:07 run (09:14 start): performance with every saber moving. New stage pose: swrp_saber_stage demo all
  sets the seven-form row running the test stand's full routine (swings, blocks, Force casts) at once. 7 sabers
  swinging with the new swept-sheet trails: 117-120 fps avg (worst frame 11-31 ms), against 120 with the row at guard;
  GPU memory 11.67 GB before play, 11.93 / 11.91 GB a minute apart while swinging (flat); no console errors. The row
  moves in lockstep (same routine, same start), so it is a worst case for effects landing on one frame.
- 2026-10-08 09:57 retry: filmed makashi/djemso/juyo with stage demo + strikeslow. The swept sheet drew OPAQUE over
  the bodies (flat blue polygons hiding the robes): the vertex_color material ignores the blend combo and alpha. Switched
  SaberTrailSheet to Material.FromShader("shaders/line.shader") (the engine's line renderer shader: unlit, two-sided,
  D_BLEND = additive by vertex alpha), real vertex alpha, one winding. Compiles; NOT yet seen in play.
  The editor ran at 8-10 fps for this whole session (from play start, before the stage was built; log: GPU fence waits
  over 250 ms, swap chain stalls) and the editor process ended abruptly at 09:58:35 with no shutdown in the log; a new
  editor started at 09:59:11. Correction: that was the lighting thread's planned editor restart (Angelo stopped play for
  it), not a crash. Check fps right after
  play_start next time and stop at once if it is under ~30.
