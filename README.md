# SWRP

Clone Wars roleplay gamemode for s&box, set aboard a Venator-class Star Destroyer.

## Playing

Open `scenes/notorious.scene` (it's also the startup scene) and press play. The character screen plays "Republic Muster", an original
synthesized theme (`SoundSource/menu_music.py`, fades out when you deploy, `swrp_menu_music` sets the volume). To use a track you
own the rights to, put it at `Assets/sounds/music/menu_custom.mp3` (or `.wav` / `.ogg`); it then plays instead. Keep such a file
local and out of anything published. Players get the character menu on join:
enlist up to 3 troopers, pick one and deploy (enlisting a new one deploys you as it straight away). Characters, ranks and
inventories are saved on the host.

Before you deploy, the character screen sits over a hangar bay of its own, far below the ship where nobody walks
(`SwrpMenuStage` and `models/hangar/menu_hangar`, the ship's own panels round a window onto space; local to you): your
selected trooper (or the class and battalion you're enlisting) stands under a spotlight with a DC-15A at the low ready; behind
him a Republic heavy fighter sits on its gear beside a maintenance gantry while a mechanic welds under the nose, astromechs and
a mouse droid roll about, and a row of troopers stands at ease for inspection as an officer walks the line. The fighter,
gantry and service cart are original models from `ModelSource/hangar_props.py` (Blender). Opening the screen in game (F4)
keeps the starfield. Standing still with a rifle out, troopers hold it at a relaxed low ready across the body
(the same pose as the sprint carry); moving, aiming or firing brings it up. Dev: `swrp_menu_stage 1 [x y z facing]` builds it while you play and films it with a hidden camera (prints
the id for the editor's camera screenshot; `0` removes it), `swrp_menu_stage_yaw <trooper> <fighter> <line>` turns the set.

| Key | Action |
|---|---|
| Esc | The Escape menu: a holo display hanging in front of you over the blurred game (it turns a little with the mouse). Resume, Scoreboard (everyone on the server by unit, with rank, class and ping), Characters (the F4 screen), Settings (fading HUD, squad markers, visor, cinematic camera and look, living ship, passing ships, helmet voice, menu sounds, ambience, radio and menu music volume, third person distance; saved on your machine) and Exit (disconnect or quit). Esc also closes the inventory and the character screen. `swrp_escape_preview <page|0>` shows it without being deployed (dev) |
| I | Inventory |
| F4 | Character menu |
| O | Staff panel (staff only; same as `/staff`) |
| Enter | Chat |
| 1-5 | Quick slots: take out what's on that slot of the quick bar (lightsaber, primaries, sidearms, then holdable gear such as the datapad); the same key again puts it away |
| Mouse wheel / Mouse 4-5 | Go through everything on the quick bar (first and third person) |
| H | Put away what's in your hands, or take the last thing out again |
| Z | Go prone or get up (Jump also gets you up); you crawl slowly while down |
| F | Weapon light on or off (DC-15A); everyone sees it and it lights the room |
| C | Third person view |
| Alt (hold, third person) | Free look: orbit the camera while your trooper keeps its heading; Alt + wheel zooms (`swrp_thirdperson_distance`) |
| Mouse1 / R | Fire / reload (each reload uses one blaster cell) |
| G | Drop what's in your hands: blaster, lit saber or held gear (it lands as a world item) |
| Mouse1 (holding gear) | Act it out for everyone near (`/me checks their datapad.`) |
| T | Throw a thermal detonator (needs one in your inventory; also Throw on it in the inventory) |
| E (on a dropped item) | Pick it up |
| F8 | Hide the whole HUD for screenshots (again to bring it back; same as `swrp_hud 0`) |
| V (hold) | Talk (push to talk) |
| B | Voice range: whisper, normal, yell |
| Y | Voice on the radio: while on, V talks on your radio net instead of just through your helmet |
| N | Comm receiver on or off (off: you don't hear radio voices) |
| Tab (hold) | Mission board and scoreboard (orders, everyone aboard, squads); with the fading HUD on, also shows every HUD readout |
| F2 | The guide: first steps, every key, directions (also `/guide`, `/keys`, `/dir <place>`) |

Chat: plain text is spoken in character and only heard within about 23 m. `/me <action>` is a local emote (`* SGT Hardcase checks his rifle.`), also heard only nearby. `/ooc <message>` (or `//`) goes to everyone.

The camera has head bob, breathing, strafe lean, look inertia and a landing dip in first person, and in either view it
shakes when you are hit (and a little when a bolt lands right beside you); a heavy hit (30 or more) leaves your ears
ringing and the world muffled for a few seconds. Deploying from the character screen flies the camera in from above and
behind your trooper, and when you die the view drifts slowly up off your body while the colour drains away.
`swrp_cinematic 0` turns all of it off (`swrp_ring` tests the ringing). Code: `Code/Player/CinematicCamera.cs`
(`SwrpShake.Add`, `AddAt`, `Ring` and `Blast( position, power, radius )` for anything that should shake the view).

Game feel (`Code/Feel`, rules in `STYLE_GUIDE.md`): a hit flinches your view away from where it came from and flashes the
screen edges red (pale blue for a stun); a heavy hit (30+) also blurs the view for a moment with a low thump; at 30 health
or less the edges pulse with the heartbeat. Bodies are thrown by the shot that killed them. Deploying fades through black
into the fly-in, and the character screen opening or closing in game cuts through black. Bolts that pass close to your
head whizz by; footsteps crunch on Tatooine sand and Hoth snow and splash on Kamino's platforms; bolt impacts add a
metal ring, a sand puff or a snow hiss; doors you
stand near rush with air and your turbolift whooshes up the shaft. The console menus (lift, brig, ship systems, GM,
battalion, spawn) fade and rise in. All sounds are synthesized by `SoundSource/feel_sounds.py`. Switches in the table
below (`swrp_feel_*`); `swrp_feel_test <effect>` tries one on your own view without damage.

## Admin commands (host console)

The host is always an admin. Add more with `swrp_addadmin`.

| Command | What it does |
|---|---|
| `swrp_whitelist <steamid or name> <faction>` | Allow a player to enlist in a legion |
| `swrp_unwhitelist <steamid or name> <faction>` | Remove that access |
| `swrp_whitelist_list` | Print admins and the whitelist |
| `swrp_setrank <steamid or name> <rank #>` | Promote/demote the player's current character (0 = Cadet ... 10 = Commander) |
| `swrp_addadmin <steamid or name>` | Make someone an SWRP admin |
| `swrp_factions` | List faction ids |
| `swrp_hurt <amount>` | Damage your own trooper for testing (negative heals) |
| `swrp_clone_anims 0/1` | Your view: stock s&box weapon animations (0) or the clone set (1, default) |
| `swrp_anim_test` / `swrp_anim_seq` | Local animation test stand (see Combat) |
| `swrp_thirdperson_distance`, `_side`, `_height` | Your third person camera: distance (Alt + mouse wheel changes it in third person, 60-300), shoulder offset (-40 left, 40 right, 0 centre) and height. Saved per machine |
| `swrp_cam <x> <y> <z> [pitch] [yaw]` | Before you deploy: move the camera to map coordinates (screenshots, the test stand) |
| `swrp_npc_max <n>` | Most NPCs alive at once (default 30) |
| `swrp_battalion_officer_rank <n>` | Lowest rank with battalion officer tools (default 6 = 2nd Lieutenant) |
| `swrp_defcon_lockdown 0/1` | Whether DEFCON 1 locks every door to 2nd Lieutenant and up (default 1) |
| `swrp_menu_music <0-1>` | Character screen music volume, your machine only (0 = off, default 0.5) |
| `swrp_ambient_volume <0-1>` | Ship ambience, distant sounds and footsteps, your machine only (0 = off, default 0.5; room tones sit quietly in the background) |
| `swrp_club_volume <0-1>` | Cantina club music, your machine only (0 = off, default 0.8) |
| `swrp_club_strobe 0/1` | Show the club strobe flashes, your machine only (default 1) |
| `swrp_club 0/1` | Host: put the DJ booth in the cantina (default 1; takes effect when play starts) |
| `swrp_club_play <id or URL>` | Host: put a club track on deck A now (underlevel, hyperlane, outer_rim, kessel, or a URL) |
| `swrp_club_autoplay 0/1` | Host: start Underlevel 1313 on deck A when the club comes up (default 1) |
| `swrp_club_info` / `swrp_club_probe` | Print what each deck is doing on your machine / check the sound system plays a built-in track (3 s, near silent) |
| `swrp_postfx 0/1` | The whole cinematic post-processing stack (area grade, bloom, grain, vignette, aim focus); 0 for low-end PCs |
| `swrp_postfx_info` | Print the current area, grade and tint |
| `swrp_fx_lights 0/1` | Blaster bolts, muzzle flashes and impacts light up the room (your machine, default 1; 0 for slower PCs) |
| `swrp_light 0/1` | Dynamic lighting framework (`Code/Lighting`): flashes, bolts, sabers, blasts and hull breaches light the room (your machine, default 1) |
| `swrp_light_max <n>` / `swrp_light_shadows <n>` | Most dynamic lights at once (default 20, bounce included) / how many may cast shadows (default 2) |
| `swrp_light_bounce 0/1`, `swrp_light_embers 0/1`, `swrp_light_breach 0/1` | Fake tinted bounce light, lingering impact embers, space light through hull breaches |
| `swrp_light_range`, `swrp_light_intensity` | Draw distance (default 3200) and brightness scale (default 1) of dynamic lights |
| `swrp_light_exposure_floor <e>`, `swrp_light_ambient_scale <k>`, `swrp_light_min_ambient/_exposure/_lamps <k>` | Readability floors: lowest auto-exposure (default 2.4, keeps rooms readable when holograms or screens are in view), ambient scale everywhere (default 1.25), and the darkest a light zone may go (0.35 / 0.7 / 0.3) |
| `swrp_light_zones 0/1`, `swrp_light_fog 0/1`, `swrp_light_ambient <k>` | Light zones (dark rooms), their volumetric haze, and a dev override of the ambient multiplier everywhere (-1 = zones) |
| `swrp_light_debug 1`, `swrp_light_info` | Overlay of active (green), shadowed (yellow), bounce (cyan) and culled (red) lights with the cost; print the same |
| `swrp_light_zone_here [ambient] [haze] [size] [exposure] [lamps]`, `swrp_light_zone_clear`, `swrp_light_zones_list` | Dev: a dark, hazy zone round the camera (local), remove them, list zones |
| `swrp_light_test [r g b] [intensity] [radius] [s]`, `swrp_light_burst [shots]`, `swrp_light_barrage [s] [rate]` | Dev: a test light where you look; fake blaster fire, one burst or sustained with bolts (lights only, no damage) |
| `swrp_space_ships 0/1` | Small ships flying past the viewports, your machine only (default 1) |
| `swrp_hyperspace <destination> [seconds]` | Jump the ship (same as `/hyperspace`): `coruscant`, `kamino`, `geonosis`, `christophsis` |
| `swrp_space_flyby [type] [lane]` | Your view: send a flight past now (type 0 fighters, 1 shuttle, 2 frigate; lane 0 +X side, 1 -X side, 2 across the bow) |
| `swrp_space_sky [material]` | Your view: show another sky material (no argument goes back) |
| `swrp_look` | Print what the camera is looking at (object, distance, model, surface) |
| `swrp_probe <x> <y> <z>` | Print the floor below a point and how far the walls are (for placing things) |
| `swrp_ship_life 0/1` | The ship's cosmetic life: flickering lights, steam, sparks, wall screens, ambient crew (your machine, default 1) |
| `swrp_life_info` / `swrp_crew_info` | List where the living ship's pieces and crew ended up |
| `swrp_life_sparks` | Make every sparking panel spark now |
| `swrp_armory_info` | List the armory racks and crates, their stock and where they are |
| `swrp_radio_volume <0-1.5>` | Volume of voices heard over the radio (your machine, default 1) |
| `swrp_helmet_voice 0/1` | Helmet filter on nearby voices (your machine, default 1) |
| `swrp_radio_tx`, `swrp_comms` | Same as Y and N |
| `swrp_comms_info` | Print the voice mixers and everyone's net, radio and receiver state |
| `swrp_comlink 0/1` | Show the comlink (the `/coms` chat command toggles it; default 0) |
| `swrp_comlink_preview 0/1` | Show the comlink with sample traffic before you deploy (screenshots) |
| `swrp_pod_info` / `swrp_pod_demo` | List the bacta pods and who is in them / play the pod animation on every pod (your view) |
| `swrp_cinematic 0/1` | The cinematic camera: bob, sway, shake and ringing, deploy fly-in, death drift (your machine, default 1) |
| `swrp_ring [0-1]` | Your view: test the ringing ears and muffled sound of a heavy hit |
| `swrp_visor 0/1` | Helmet visor overlay in first person: faint T-visor edges and scanlines (your machine, default 0) |
| `swrp_hud_fade 0/1` | Fade HUD parts you don't need: ammo shows when you fire, reload, switch or run low; vitals when you're hurt (default 1) |
| `swrp_hud 0/1` | Hide the whole HUD, nametags too, for screenshots (F8 toggles; menus still open) |
| `swrp_feel 0/1` | The whole game feel module (`Code/Feel`): hit reactions, screen transitions and feel sounds (your machine, default 1) |
| `swrp_feel_kick 0/1` | Hits flinch your view away from where they came from (default 1) |
| `swrp_feel_hitfx 0/1` | Red edge flash on hits, blur on heavy hits, low-health pulse (default 1) |
| `swrp_feel_transitions 0/1` | Fade through black on deploy and when the character screen opens or closes (default 1) |
| `swrp_feel_ragdoll 0/1` | Bodies thrown by the killing shot (default 1) |
| `swrp_feel_sounds 0/1` | Footsteps on planet ground, impact layers per surface, debris, door and lift whooshes (default 1) |
| `swrp_feel_whizz 0/1` | Bolts passing close to your head whizz by (default 1) |
| `swrp_feel_volume <0-1.5>` | Volume of the feel sounds (default 1) |
| `swrp_feel_test <hit\|heavy\|stun\|lowhp\|deploy\|dip\|flash\|whizz\|step\|impact\|debris\|door\|lift>` | Your view only: try one feel effect (no damage, nothing networked) |
| `swrp_feel_info` | Print the feel switches and current state |

Battalion ids (the commands still call them factions): `clone_trooper` (unassigned shinies, open to everyone), `501st`, `212th`,
`104th`, `327th`, `41st`, `coruscant_guard`. Every battalion is a sub-division of one faction, the Grand Army of the Republic
(GAR). Who fights whom lives in `Code/Data/Allegiances.cs`: the GAR is friendly to the Republic Navy and the Jedi Order and
hostile to the CIS. Friendly fire (and the NPCs to come) read that table. Every trooper wears their battalion's colour as markings
(helmet ridge, ear discs, shoulders, arm bands), taken from the battalion's `ColorHex`; the masks come from
`X:\SWRP Projects\Clonetrooper\sbox_build\battalion_markings.py`.

Saved data lives in the s&box data folder under `swrp/` (`whitelist.json`, `characters/<steamid>.json`). Edit
`whitelist.json` by hand while the server is off if you prefer.

## Chat and roleplay

Enter opens chat (the engine's own chat box is turned off in ProjectSettings/Platform.config). Type /help in game.

| Command | Who hears it |
|---|---|
| plain text | everyone within your voice range (B cycles Whisper / Normal / Yell; the mic uses the same range) |
| `/w` `/y` | whisper (very close) / yell (far) |
| `/me` `/do` `/roll [max]` | emote, scene description, dice roll, nearby |
| `/radio` (`/r`) | your whole legion, any distance |
| `/broadcast` (`/bc`) | the whole ship; Captain and up, or staff |
| `/ooc` (`//`) `/looc` | out of character, everyone / nearby |
| `/examine` | description of the nearest trooper |
| `/voice` `/holster` `/draw` `/clear` | yourself |

H puts away and takes out again what's in your hands (blaster, saber or gear).

## Staff

Roles are User, Trial Moderator, Moderator, Admin and Senior Admin (the host is always Senior Admin). Set them with
`/setrole <player|steamid> <user|trial|moderator|admin|senior>`; they are saved in `swrp/whitelist.json`. You can only give
roles below your own, to people below your own. Staff can only act on people below their own role, and never on the host.

Trial Moderator: `/kick /mute /unmute /gag /ungag /freeze /unfreeze /bring /goto /return /spectate /ranks`, the staff panel,
reports and staff chat.

Moderator: also `/ban (up to 1 day) /mute /unmute /gag /ungag /jail /unjail /freeze /unfreeze /slay /respawn /bring /goto /return /spectate /ranks`
Admin: also permanent bans, `/unban`, `/setname /setdesc /setfaction /setrank /promote /demote /setrole /give`
Senior Admin: also makes Admins, and sets the minimum role for any staff command in the staff panel's PERMISSIONS tab
or with `/perm <command> <trial|moderator|admin|senior|default>` (saved in `swrp/permissions.json`; `/perm` and `/setrole`
themselves can't be changed).

Durations are like `30s`, `10m`, `2h`, `3d` or `perm`. Bans, mutes and gags persist in `swrp/punishments.json`. /jail
sends people to the 12 brig cells (SwrpJailPoint objects in the scene, one in each real cell). Every staff action is shown to online staff.

**Staff panel (O, or type `/staff`; Moderators and Admins)**: every online player with Steam name, character, CT number, faction, rank, role
and ping, their recent logged actions, and buttons for kick, ban, mute, gag, jail, freeze, slay, respawn, bring, goto,
return and spectate (with duration and reason fields). Admins also get the deployed character's name, description, faction
and rank, all of that player's characters, and whitelist toggles per faction. Tabs for reports, the staff log and every
whitelist entry with who granted it. The buttons type the same chat commands, so permissions are identical.

**Toolkit**: `/duty` (off duty drops all powers), `/noclip` (WASD along your view, Space up, Ctrl down, Shift fast), `/god`,
`/invis` (body, nametag and gun hidden from others), `/tp <x> <y> <z>`, `/announce <text>` (server-wide banner),
`/stafflog [n] [player]`, `/reports`, `/report claim <id>` and `/report close <id> [note]`. Admins: `/whitelist` and
`/unwhitelist <player|steamid> <faction>` (same as the console commands, and they record who granted it).

**Hotkeys**: the staff panel's HOTKEYS row puts noclip, god mode and invisibility on keys of your choice (click the key,
press the new one; Escape or a click cancels; ✕ clears it). Each staff member's keys are saved on their own machine in
`swrp_staffkey_noclip` (default `v`), `swrp_staffkey_god` and `swrp_staffkey_invis` (both unbound by default). They only work
on duty, and a press types the same command as the button, so the host still checks everything. A hotkey on the push-to-talk
key (V) fires on a quick tap; holding it still talks. A key that another game action also uses shows a warning.

**Staff chat**: `/sc <text>` (or `/staffchat`) goes to every online staff member and the staff log.

**Reports**: anyone can `/report <player> <what happened>` (one every 30 s); online staff get a ping and it shows in the panel.

**Safety**: no staff action can target the host, or anyone with an equal or higher role. Every staff action is logged with
who, target, action, details and time in `swrp/stafflog.json`. Rate limits: 4 bans per 10 min, 8 kicks per 5 min, 10 slays
per 2 min, 10 jails per 5 min, 5 role changes per 10 min, 4 announcements per 2 min (the host is exempt).

Rank ladder: Cadet, Private, Private First Class, Corporal, Sergeant, Staff Sergeant, 2nd Lieutenant, 1st Lieutenant,
Captain, Major, Commander. `/setrank` takes the number (1 = Cadet), name or abbreviation.

## Units, orders and comms

**Squad menu** (**F3**, or `/squad` on its own): a holo display of your squad. The header shows its colour, name and its own radio
net (frequency, an equaliser that jumps while someone talks, who is talking); a radar puts every squadmate around you as you face
(pulsing dots, the leader ringed, downed troopers blinking red, the rally point as a diamond); member cards slide in with rank,
class, health, status (ready, bleeding, down, offline), deck and distance, and a waveform while they talk. Comms buttons tune
you to the squad net, switch your voice between helmet and radio (Y) and your receiver (N), and a log shows the net's traffic
with a box to send on it. Leaders get MAKE LEAD and REMOVE on each card, an invite list (troopers aboard without a squad,
nearest first), rename, eight colours, RALLY AT CROSSHAIR (a marker on every member's HUD; `/squad rally [clear]`) and DISBAND
(click twice). Without a squad it shows invites waiting (ACCEPT / DECLINE, also `/squad decline`), lets you form one, and lists
the squads aboard. **Each squad has its own net**: forming or joining puts your radio on it (`/channel squad`), leaving puts you
back on your legion's. Every button types the same command.

**Squads** (up to 8): `/squad create [name]`, `/squad invite <player>` (they type `/squad accept`), `/squad leave`,
`/squad kick <player>`, `/squad promote <player>` (hand over lead), `/squad rename <name>`, `/squad color <#hex>`,
`/squad disband`, `/squad list`. Your squad shows on the left of the HUD (name, CT number, health) and squadmates get a
coloured marker over their heads (`swrp_squad_markers 0` hides them on your screen). Squads last for the session.

**Platoons** (2nd Lieutenant+): `/platoon create [name]`, `/platoon add <squad>`, `/platoon remove <squad>`,
`/platoon disband`, `/platoon list`. Squad members see their platoon and its commander under the squad name.

**Orders** (Captain+): `/order <title> | <objective> | <rally point or "here"> | <low|normal|high|critical>` posts the
active order for everyone (top of the HUD, with the distance to the rally point when it is "here"). `/order complete [note]`,
`/order fail [note]` and `/order cancel` close it. Every order is logged in `swrp/orders.json`. **Hold Tab** for the
mission board: active order, recent orders, platoons and squads.

**Radio**: `/r <message>` talks on your channel, `/r <channel> <message>` on another, `/channel <squad|legion|command|open>`
switches (command is Lieutenant+). `/dispatch <channel> <message>` (2nd Lieutenant+) sends a highlighted dispatch. Radio
lines crackle and light the radio indicator above the voice label. Muted players can't use chat or radio; gagged players
can't transmit on the radio either.

**Ranks**: Captains and above can `/promote` and `/demote` troopers in their own legion, but only below their own rank (a
Captain can promote up to 1st Lieutenant); the Commander can promote anyone; admins use the staff commands as before. Every
rank change (officer, staff or console) is logged in `swrp/ranklog.json`; `/ranklog [name]` (2nd Lieutenant+ and staff)
shows the last ten.

**Briefings**: stand at a holo-table (war room, bridge or briefing hall) and type `/briefing <title> | <text>`
(2nd Lieutenant+); everyone nearby sees it as a hologram over the table. `/briefing clear` takes it down.

## Combat

Each class carries its own blasters (set in `Code/Data/Classes.cs`, issued on deploy if missing): Trooper DC-15A rifle and
DC-17 pistol, Heavy Z-6 rotary blaster, Specialist Valken-38X sniper rifle and DC-17, Medic DC-15S carbine and DC-17 (plus
the defibrillator and medkit). The models are Battlefront II's
(`ModelSource/bf2_props.py`, local only).

Any blaster in your inventory can be used, not only your class's: one from the Q menu, an armory rack or the floor works
in your hands. Picking one up with your hands empty takes it straight out. Class weapons are issued once per class (a gun you
drop stays dropped). The inventory (I) is a compact kit: the quick bar (what 1-5 take out) along the top, a 24-slot grid
(each item shows its quick-slot key and count; double-click uses or takes it out, right-click drops one), and a one-line
detail strip with Equip / Hold / Use / Throw, Drop and Drop all. Items the game no longer knows show a red ? and can be
discarded. Everything in the kit can be dropped and picked up again with E; on the floor each item shows its model.
The HUD's ammo panel lists the quick bar under the magazine, the thing in hand lit. Code: `Player/SwrpHands.cs` (what's
in hand), `Data/ItemModels.cs` (item models), `Data/Items.cs` (`Holdable`, `HoldType`, `HoldAction` for new gear).

**The weapon base** (`Code/Data/Blasters.cs`): every blaster, Republic and Separatist, is one `BlasterDefinition` in
`BlasterRegistry`, written in the stats on its weapon card (the inventory shows them as bars): Accuracy (aimed cone in
degrees, lower is better), Hip fire (cone from the hip, lower is better), Fire rate (rounds a minute), Mobility (move speed
carrying it, percent), Damage (per bolt; a scatter blaster's is per pellet, `Pellets` bolts a shot), Stability (recoil, percent
of the standard kick, lower is steadier) and Drop-off (full damage to `DropOffStart`, easing to `DropOffMin` percent at
`DropOffEnd`). An entry becomes an inventory item, a Q-menu weapon and something an NPC can carry (`NpcDefinition.WeaponItem`);
troopers' and droids' shots both use its damage over distance. Guns: DC-15A, DC-15S, DC-17, Z-6, DC-15X sniper, DP-23 scatter
blaster; Separatist E-5 (the B1s carry it), E-5s sniper, DT-57 pistol. A new gun is a new entry plus a model.

Shots look and sound the part: each bolt reads as light rather than a solid shape, a hair-thin hot filament inside a haze of
soft glow sprites (brightest at the head, fading to nothing at the tail) that blooms and lights the walls as it flies, muzzles flash, and impacts burst with a glow, sparks and a flash of light (`SwrpFx` in `Code/Player/SwrpCombat.cs`).
It stays cheap with a full server: at most 14 effect lights at once, none far from your camera, a cap on sparks, and fewer
extras for shots far away; `swrp_fx_lights 0` turns the lighting off on slower PCs. Reloading is heard by everyone nearby
in three steps timed to each weapon: the spent cell vents and pops out, a fresh one slides in and locks, and the
capacitor charges to a ready chirp.

Clones handle rifles with their own animation set: low-ready idle with breathing, shouldered aim while moving, a
kneeling crouched aim and a recoil that the shoulder absorbs (the legs stay on the s&box motion-capture locomotion). It
lives in the `clone_<class>_drill.vmdl` models; `swrp_clone_anims 0` switches your view back to the stock s&box set.
On two-handed rifles the left hand holds the real model's handguard (`SwrpEmotes.Foregrips`, per rifle model in its grip
frame); the clone clips were made for the original DC-15A, whose foregrip sat back by the trigger. NPC clones and the test
stand use the same grip, and the third-person rifle is sized with the trooper. Dev tools for poses, all local and read-only for
everyone else: `swrp_pose_stage <emote 0-3> [holdtype] [carry 0/1] [view yaw] [distance] [view height] [speed] [x y z]` puts a
test trooper somewhere quiet (default: the engineering corridor) with its own camera that never draws to the screen (render
it with the editor bridge's camera screenshot by the id it prints; `swrp_pose_stage -1` removes it), `swrp_gun_profile [item]`
prints a rifle's side profile and where the stage's hands sit on it, and `swrp_grip`, `swrp_salute`, `swrp_atease`, `swrp_thumb` and `swrp_carry` tune the
rifle grip, salute lines and at-ease hands live.

Sprinting (Shift) with a rifle drawn carries it low across the body: the grip by the right hip, the barrel angled down and
across to the left, the left hand still on the foregrip, bouncing a little with each stride; aiming, firing or reloading
brings it straight back up. It is posed in code from the arm bones (`SwrpEmotes.CarryPose`), so it works for every rifle on
both animation sets, and your first-person rifle lowers and turns across the view the same way.
`swrp_anim_test [holdtype] [duck] [speed] [fire every s] [yaw]` puts a local clone in front of you to look at the
animations from any side (`swrp_anim_test -1` removes it, `swrp_anim_seq <sequence>` plays one sequence on it). Sources,
Blender scripts and the validation report: `X:\SWRP Projects\Clonetrooper\sbox_build\anim` (local only, like the models).

| Key | Action |
|---|---|
| 1-5, wheel | Quick slots / cycle (see the key list at the top) |
| H | Put away or draw |
| Mouse1 | Fire (rifle, carbine and Z-6 are automatic; the pistol is one shot per click) |
| Mouse2 | Aim down the sights: zooms, tightens spread, steadies recoil |
| R | Reload (one blaster cell refills the magazine; sprinting, holstering or switching cancels it) |
| X | Stun setting on and off (not on the Z-6): stunned troopers drop for a few seconds, no damage |

The host traces every shot with spread (wider from the hip and on the move) and applies damage: headshots x2, legs x0.75,
armour soaks most of a hit before health. Friendly fire is off by default: blasters don't hurt troopers on the same side,
but stun rounds still work (MPs, the brig). Turn it on with `swrp_friendlyfire 1` on the host. Each weapon's magazine is
saved on the character; the HUD shows magazine / reserve rounds and cells. Shooters get hit markers (amber for headshots,
red for kills), victims get a damage-direction arc and a screen flash, and kills go to the kill feed (top right). Sounds
(fire, stun, reload, empty click, impact, hit marker, hurt) are synthesized in `SoundSource/ui_sounds.py`. The DC-15A's shot (`blaster_rifle`, also used by clone
NPCs) is built like a struck wire: a falling chirp from about 3 kHz with slightly inharmonic metallic partials, a short crack, a
punchy low body and a tight slap back (`wire_chirp` and `blaster_dc15a` in the script).

**Thermal detonators.** Troopers carry them in their kit (`thermal_detonator`); **T** (rebindable, "Throw a thermal detonator")
or Throw on it in the inventory lobs one along your view. It arms with a chirp, bounces, and its red lamp blinks and beeps
faster until it goes off three seconds later: deadly within about 60 units, falling away to nothing at 380, and only for those
with a clear line to it (walls and cover stop it). It follows the same friendly-fire rule as blasters, and the thrower is never
spared. The blast flashes and lights the room, throws sparks and smoke, scorches the floor, shakes the view, rings the ears of
anyone close and throws loose objects about. Kills show as Thermal Detonator in the kill feed. `swrp_grenade_test` (host)
throws one from the camera. Code: `Code/Player/SwrpGrenade.cs`.

Admin and testing: `/give <player> <item id> [count]` (e.g. `/give Cub blaster_cell 10`), `swrp_dummy` puts a training dummy
in front of you (it stands back up after being dropped), `swrp_dummy_clear` removes them, `swrp_stun` is the same as X,
and `swrp_test_fire [shots]` (host only) fires your drawn blaster as if you clicked.

## Jedi: lightsabers and the Force

Only Jedi Order characters. Build the saber when you enlist (the LIGHTSABER rows under PADAWAN): kyber crystal (colour and
feel: blue balanced, green calm, yellow quick, violet aggressive, cyan precise, amber steady, white defensive; no red),
single / dual with a shoto / saberstaff, blade length, emitter, grip, pommel and finish. Sizes stay real against the body
(11-unit / 28 cm hilt, 0.86-0.99 m blade). Put away, the hilt hangs at your hip.

| Key | What it does |
|---|---|
| 1 (quick slot) / H | ignite / retract (the saber is first on the quick bar; the wheel and 2-5 switch to blasters) |
| Mouse 1 | attack: the way the mouse was moving picks the cut (left, right, up = overhead, down = thrust; no flick alternates); tap for light, hold to charge a heavy; in a blade lock, mash it |
| Mouse 2 | hold to guard; raise it just before a blow lands to parry; guards also deflect blaster bolts back |
| R | next lightsaber form you know |
| F | use the selected Force power (hold for Lightning) |
| Z (hold) | Force wheel: move the mouse towards a power and let go |
| Mouse 3 | throw the saber (Saber Throw skill); press again to call it back |
| P | Jedi training: level, experience, skills and forms (`swrp_jedi_menu`) |

F, Z, Mouse 3 and P are new input actions: they work after the editor/game restarts with the new `Input.config`.

Combat is host-authoritative. Swings chain into combos per form (Shii-Cho, Makashi, Soresu, Ataru, Djem So, Juyo: data
in `Assets/data/jedi/forms/*.sform`), cost focus, and hit along the blade's whole sweep each frame (fast swings can't pass
through anyone), once per target per swing, harder the faster the blade moves. Guarding costs focus per blow; at zero your
guard breaks. Two Jedi cutting into each other lock blades: mash to win, the loser is thrown back. Blades scorch walls and
floors where they touch (they never cut the ship). Experience comes from fighting; each level is a skill point.
Training is saved per character on the host (`swrp/jedi/<steamid>_<character>.json`, with a `.previous.json`).

Settings: `swrp_jedi_sparring 1` (host: Jedi blades hurt other Jedi even with friendly fire off, never below 15 health),
`swrp_jedi_lightning 0` (host: allow Force Lightning), `swrp_jedi_hitstop 45` (your view: hit-stop in ms).
Game Masters: the GM menu (J) has MAX OUT LIGHTSABER TRAINING (`/gm jedi max`): your deployed Jedi gets level 30, every
form, technique and power, and full focus and Force, for testing.
Testing: `swrp_saber_test [crystal 0-6] [blade 0-2] [form id] [emitter] [finish]` puts a local Jedi stand in front of you
running every move (`swrp_saber_test -1` removes it). Code: `Code/Jedi/` (animgraph spec in `Code/Jedi/ANIMGRAPH.md`), sounds
`SoundSource/jedi_sounds.py`, textures `ModelSource/jedi_textures.py`, forms `ModelSource/jedi_forms.py`.

## Injuries and medics

At 0 HP you go **down** instead of dying: you can crawl, **E** calls a medic (a radio call to medics and your squad, and a
marker on medics' HUD), and **holding K** for two seconds gives up. If nobody revives you before the bleed-out timer ends
(`swrp_bleedout`, default 90 s) you die, leave a ragdoll, and redeploy at the nearest medbay bed after `swrp_respawn_time`
seconds (default 8; `swrp_respawn_medbay 0` uses the faction spawn instead). A downed trooper hit again hard enough is killed.

**Reviving**: hold **E** on a downed trooper. Anyone can do it in 9 seconds and leaves them at 20 HP and still bleeding; the
**Medic** does it in 3.5 seconds, at 50 HP, with the wound closed. A Medic with the **defibrillator** in hand (quick bar)
revives in 3 seconds at full health; with the **medkit** in hand, hold E on a trooper who is up but hurt or bleeding: 4 seconds,
full health, bleeding stopped (both are Medic-only and never run out). Moving away, putting the tool away or taking a hit
interrupts it. Staff `/heal` still works as before.

**Bleeding** starts when one hit takes 18+ health; you lose about 1 HP a second until it's treated. Medical items (from the
inventory, or bind `swrp_use <item id>`): **bacta patch** (1.5 s, +25 over 6 s, stops bleeding), **bacta injector** (0.8 s,
+35, stops bleeding), **field medkit** (4 s, +20 and +50 over 10 s, stops bleeding). Moving away while applying cancels.
**Bacta stations** in the medbay, the hangar and the barracks corridor: press E for a 2.5 s treatment that stops bleeding and
heals 45 over 12 s, then a 60 s cooldown per trooper.

**Bodies** stay for about 75 seconds (at most 12 at once), then sink away. `/examine` next to a body shows whose it is; medics,
officers (2nd Lieutenant+) and staff also see the cause of death. Low health brings a red vignette and a heartbeat.

Console: `swrp_callmedic`, `swrp_giveup`, `swrp_use <item id>`; host only for testing `swrp_test_revive`, plus `swrp_hurt <n>`.

## Doors and access

The ship's doors work. Walk up to a door or the keypad beside it and press **E**; a prompt next to it shows the door's name and
who may open it (green: open access, amber: rank-restricted, red: locked or not for you). The host checks every request against
your current character; a denied attempt plays a buzz and shows a short message. Doors close themselves after a few seconds
(not while someone stands in the doorway).

| Who | What |
|---|---|
| Everyone | Barracks, medbay, cantina, hangar entrances, corridors |
| Corporal+ | Armory, turret access |
| Sergeant+ | Brig, engine room, shield generator, centrifuge |
| 2nd Lieutenant+ | Bridge, bridge control rooms, interrogation, brig records |
| Captain+ | Bridge offices, High Command, the 12 brig cell doors |
| Staff | Every door, even locked ones |

`/lock` and `/unlock` (Captain+ or staff) lock the door you are looking at. Locks are saved per map in
`swrp/doors_<map>.json` and survive restarts. The main hangar bay doors are run from the **hangar control console** (press E on
it); it sounds an alarm and announces the change to everyone. `/jail` still drops people into the brig cells, behind the cell
doors.

Console: `swrp_door_use <door id|hangar>` (same as pressing E at that door), `swrp_door_check <door id>` (would your character
get through, ignoring staff rights), `swrp_door_info <door id>` (state, rule and part positions), `swrp_door_gaps [door id]` (host: lists see-through gaps around closed doors), `swrp_doorway_check [door id or name]` (read-only: sweeps a
trooper-sized box through every doorway and names anything in the way). For building and testing: `swrp_tp <x> <y> <z> [yaw]` (host only)
moves your trooper to map coordinates, `swrp_chatlog [n]` prints recent chat to the console and `swrp_interact_debug 1` logs
what the E prompt is looking at. Door ids, names, access
levels and positions are listed in `MapSource/doors_review.md`; change them in `RULES` in `MapSource/notorious_doors.py` and
rebuild the map. Doors also accept a keycard: set `KeycardItem` on a door (or `SwrpDoor.KeycardHook` from code) and anyone
carrying that item gets through.

## Brig shields

The brig's blue shields (the 12 cell gates, the two entrance rays and the three fight-cage walls) can be switched by
**Captains and up, the Coruscant Guard** (the ship's shock troopers and military police) **and staff**:

- **The brig tower** in the middle of the cell block has a button per cell (and the original map's ray and fight-cage buttons):
  E on one raises or lowers that shield.
- **The shield control** at the console ring in the brig's first room: E opens a list of every shield with a switch each, and
  all-cells buttons.
- Each cell's own panel (outside in the corridor only; the one inside the cell is gone) works for the same people.

Shields stay as they are set (they don't close on their own), and every switch is written to the staff log.

## Turbolifts and teleporters

The Venator's two turbolift shafts work. Each deck a shaft serves has a lift door behind the main corridor: press **E** on it to
call the car (the door opens), step in, and press **E** on the deck panel on the car's back wall. Pick a deck in the menu (click it
or press its number, Escape closes). The doors close and lock, the car hums for a few seconds, and everyone aboard arrives in the
same spot on the chosen deck as its door opens with a chime. Only one ride runs per shaft at a time, and nobody can open either
door while it does.

| Shaft | Decks |
|---|---|
| Port turbolift | Main Deck, Operations Deck, Command Deck, Jedi Temple level |
| Starboard turbolift | Engineering Deck, Main Deck, Operations Deck, Command Deck |

The map's teleport pads work too, where the original map had them: the two **bridge lifts** on the command deck (to the port and
starboard bridges) and the lift back down from each bridge, the **simulation rooms** off the operations deck and their exits, and the
**Jedi temple**: ride the port turbolift to its top level and step out of the car. The temple and the simulation rooms are separate
areas far above the ship, so they were brought into the port with this change. Walk into a pad to use it; arrivals get two seconds
before a pad will take them again, and troopers in noclip, frozen, jailed or downed are left alone.

Console: `swrp_transit_info` (every lift and pad), `swrp_lift_ride <port|starboard> <from deck> <to deck>` (host: run a ride, deck
numbers start at 1 from the bottom) and `swrp_transit_check` (host: checks every car and pad destination has floor and room to
stand). The lift and pad data come from the VMF (`MapSource/notorious_transit.py`, written to `Assets/data/notorious_transit.json`
by the map build); deck names are in `DECKS` there. Objects tagged `transit` (the `swrp_anim_test` stand, later NPCs) ride lifts
and use pads along with players.

## Ship systems

**Alert level.** The ship runs at DEFCON 5 (normal operations) down to DEFCON 1 (maximum alert):

| Level | Name | What changes |
|---|---|---|
| 5 | Normal Operations | nothing |
| 4 | Increased Readiness | lights a touch warmer, HUD shows the level |
| 3 | Elevated | amber lighting, alert tone |
| 2 | Battle Stations | orange lighting, a klaxon |
| 1 | Maximum Alert | red, slowly throbbing lighting, three klaxons, and **lockdown**: every door opens for 2nd Lieutenants and up only (keycards and staff still work) |

Officers (2nd Lieutenant and up) and staff set it with `/alert <1-5> [reason]` (or `/defcon`), or from the **command console** on
either bridge (E on it opens the ship systems menu). `/alert` on its own shows the current level to anyone. The change goes out as
a ship-wide line naming who set it. `swrp_defcon_lockdown 0` (host) turns the DEFCON 1 lockdown off. The level isn't saved; the
ship starts every session at DEFCON 5.

**Ship announcements.** `/pa <text>` (officers and staff) or the console's announcement box: a chime everyone hears, a banner
across the top of the screen and a ship-wide chat line. One every 8 seconds.

**Holo-map.** Each bridge's command-deck holo-table projects a slowly turning hologram of the ship's decks with a dot for every
deployed trooper aboard, in their squad's colour (battalion colour when not in a squad; squad leaders get a bigger dot). Staff
who are invisible don't show. E on the table lists every squad and which deck its troopers are on. The deck plan comes from
`MapSource/notorious_holo.py` (written to `Assets/data/notorious_holo.json` by the map build).

**Training courses.** The two simulation rooms (teleport pads on the operations deck) each have a course console just inside the
door. E starts a 40-second run: after a 3-second countdown, droid-outline targets pop up across the room, up to three at a time,
each for 4 seconds. A hit scores 100 points plus up to 200 for speed; only the runner's shots count. Leaving the room or pressing
E again ends the run. Everyone in the room sees the timer and score at the top of the screen, and the best run per trooper goes on
a top-10 board per room (`/training`, saved in `swrp/training_scores.json`). CIS droid dummies can join the course once the NPCs
exist.

Console: `swrp_alert <1-5> [reason]` and `swrp_pa "<text>"` (host, same as the chat commands), `swrp_course_demo [sim_a|sim_b]`
(host: pops up targets without a run, for checking a room).

## Battalions and their officers

**L** (rebindable) or `/battalion` (also `/bat`, `/roster`) opens your battalion's roster: every trooper with a character in
it (and players whitelisted who haven't enlisted yet), with rank, designation, role, post, whether they're online and when they
were last seen. The battalion's **leadership posts** show at the top: the **Commander** (leader), the **Executive Officer**
(co-leader) and the **Jedi General**. Staff set them from the staff panel's Battalions tab (O), or with
`/battalion post <battalion> <commander|xo|jedi> <player or none>`.

**Who can do what** (only in their own battalion):

| | Officers (2nd Lieutenant+) | Executive Officer | Commander, Jedi General | Staff |
|---|---|---|---|---|
| Whitelist players into the battalion | yes | yes | yes | yes |
| Promote, demote, set roles, remove | troopers below their rank, up to one rank below their own | up to Captain | up to Major | anyone |
| Arrange and approve transfers | | yes | yes | yes, at once |
| Act on the leadership | | the Jedi General | the Commander: the XO and Jedi General | yes |

Officers keep their rank's authority; a post raises it (the XO acts as a Major, the Commander and Jedi General as Commanders).
Nobody can act on themselves, and the officer rank is `swrp_battalion_officer_rank` (default 6, 2nd Lieutenant).

- **Roles** are short titles (Medic, ARC Trooper, Squad Lead), shown on the roster.
- **Removing** a trooper moves that character to the unassigned shinies (`clone_trooper`) and drops their whitelist for the
  battalion once they have no character left in it; nothing else on the character changes.
- **Transfers** move one character to another battalion with its rank. A Commander or XO starts one from the roster (or
  `/battalion transfer <member> <battalion>`); the other battalion's Commander or XO approves it (`/battalion approve <#>`,
  `/battalion deny <#>`, or the buttons). Staff transfers happen at once.

**Accountability.** Every action is numbered and written to the battalion history (`swrp/battalions.json`, `/battalion log`,
the roster's Recent changes), the rank log (`/ranklog`) and the staff log, and staff see it in chat. Officers are rate limited
(8 whitelists, 5 removals, 12 rank changes and 20 roles per 10 minutes, 4 transfers per 30). Staff are never limited, can act in
any battalion (`/battalion view <battalion>` or the buttons on the roster) and can reverse any action: the REVERSE button next
to it or `/battalion reverse <#>` puts the rank, role, battalion, whitelist or post back as it was.

## Staff build tools

Staff and Game Masters **hold Q** for the spawn menu (release to close; it stays open while you type in a search box, Q again
closes it). It is laid out like Facepunch's Sandbox Q menu: spawn tabs on the left (Props, Entities, NPCs, Weapons, Items, Craft)
with a category list and icon grid, and Tools / Utilities on the right (the tool list, grouped, and the picked tool's settings):
- **NPCs**: B1, B2, droideka or clone at your crosshair, 1 to 10 at a time (the same as `/gm spawn`, which every staff member may
  use, along with `/gm list` and `/gm cleanup`). Clone troopers are friendly and carry the troopers' own DC-15A: the same model,
  damage, rate of fire, range, sound and bolt (an NPC type's `WeaponItem` in `Code/Data/Npcs.cs`).
- **Weapons**: every blaster (DC-15S, DC-15A, DC-17, Z-6) and the stun baton, plus blaster cells (10) and thermal detonators (3),
  dropped where you aim; anyone picks them up with E.
- **Props**: the Star Wars crates and container and a few stock props, or any model found with the **search** box (sbox.game
  model packages). Picking one puts a see-through copy on your crosshair: **left mouse** places it standing on the surface,
  **E + mouse** turns it (**Shift** snaps to 45°), **right mouse** puts it away. Placed props have physics.
- **Tools**, ported from Facepunch's Sandbox (MIT licence, `Code/Build`):
  - **Physgun**: hold **left mouse** on a prop, NPC or dropped item to carry it; the **wheel** pulls or pushes it; **E + mouse**
    turns it (E + W/S moves it, E + A/D spins it; **Shift** snaps 45°, **Alt** 15°); **right mouse** freezes it in place; **R**
    unfreezes it and everything joined to it. **Right mouse** on a loose prop pulls it to you; then left mouse throws it.
  - **Tool gun**, pick a mode in the menu (settings appear under it): **remover**, **weld** (rigid or soft), **no collide**,
    **ball socket**, **rope** (slack, rigid), **elastic** (strength, damping), **slider**, **motor** (always on or held,
    reversed, flip axis), **keep upright**, **colour**, **trail**, **resizer**, **mass**, **gravity**, **stacker** (copies,
    R direction, right mouse world/object axes), and the gadgets: **thruster** (power, key, reverse key), **wheel** (speed,
    power, steering), **hoverball**, **balloon** (lift, rope length, shape). Constraint tools take two clicks (the second may
    be the ship itself); right mouse cancels, R removes that kind from what you aim at.
  - **Gadgets run from a seat**: place a **seat** from the **ENTITIES** tab, join it to your build (weld), press **E** on it to
    sit; your keys then drive every thruster, wheel, hoverball and motor joined to it (wheels W/S, Space brakes, A/D steer;
    hoverballs Space/Ctrl; thrusters their own key). E again stands up. ENTITIES also has **lights** and **spotlights**.
  - **Camera** (tool gun, Render group): left mouse places a stationary camera where you stand looking; **F7** (any time) looks
    through each camera in turn and back to your eyes (you see your own body meanwhile). **F8** hides everything on screen for
    screenshots: the HUD, crosshair, chat, nametags, NPC callouts, the build hint, viewmodel and placement ghost.
  - **U** undoes your last spawned prop, weapon, gadget or constraint (Z is prone). **UTILITIES** lists your undo history
    (click one to undo it) and cleans up your builds, your constraints, or (admins) every build on the server.
    "Put away" gives you your blaster back.
  Tools never touch the map, doors or players.

Spawns, removals and constraints go in the staff log.

## Enemies and Game Masters

**NPCs.** Four kinds, all data in `Code/Data/Npcs.cs`:

| Type | Side | Health | Behaviour |
|---|---|---|---|
| B1 Battle Droid (`b1`) | CIS | 55 | holds ground, fires E-5 bursts, ducks into cover when hurt or under fire |
| B2 Super Battle Droid (`b2`) | CIS | 160 + 120 armour | walks straight at you firing wrist blasters, never hides, shrugs off most of a stun |
| Droideka (`droideka`) | CIS | 180 + 260 shield | rolls fast to firing range, unfolds behind a deflector bubble (stops bolts and stuns until it drops; recharges), rolls again when it loses sight of you |
| Clone Trooper (`clone`) | GAR | 100 + 25 armour | the same as a B1, on the Republic's side, with the troopers' own DC-15A and two detonators |

**Firefights.** Riflemen (B1s and clones) holding a position spend most of a firefight down on one knee (more when hurt),
rising now and then for a better look, and a kneeling NPC is a smaller target. Each carries thermal detonators (clones two, B1s
one) and considers throwing one about every 1.5 seconds: a 40% chance to start with, more when the enemy has ducked out of
sight (to flush them out) or several are bunched together, less at close range. Throws are staggered so a squad never
throws all at once: nobody throws in the first 6 to 14 seconds of a fight, each NPC waits 16 to 26 seconds between its own,
a side throws at most once every 8 seconds with no more than two of its detonators in the air, and nobody throws where it
would land near a friend or without a clear arc. Three in four NPCs notice a live detonator near them and run clear of it.

Who shoots whom comes from the allegiance table (`Code/Data/Allegiances.cs`): droids attack every GAR, Navy and Jedi trooper and
clone NPC, and clone NPCs attack droids. They find the nearest enemy they can see, walk the ship on a navmesh, open unlocked doors,
and shoot through the same rules as players: hit zones, armour, stun rounds, the kill feed (the NPC's name and weapon show as the
killer). Destroyed droids fall as ragdolls; a droideka breaks apart. No more than `swrp_npc_max` (default 30) are alive at once.
The droids are simple original models built from primitives by `ModelSource/droids.py` (B1, B2 and a tactical-droid style
commander, rigid-skinned to the clone troopers' skeleton so they use the same animations) and carry an original E-5 from
`ModelSource/blasters.py`; the droideka is put together in code. They are stand-ins until the server's own CIS droid models
arrive: set `ModelPath` on a type in `Npcs.cs` (any model on the human/clone skeleton animates) and restart the editor.

The navmesh is built only where NPCs are spawned (a 5,200-unit square around the spot, the first time), so the rest of the ship
costs nothing.

**Game Masters.** Admins are always Game Masters; `/gm grant <player>` (admin) makes a Moderator one, `/gm revoke <player>` undoes it
(saved in `whitelist.json`). **J** (rebindable) or `/gm` opens the event menu; every button types a `/gm` command, and every Game
Master action goes into the staff log.

| Command | What it does |
|---|---|
| `/gm spawn <type> [count]` | up to 10 NPCs in a group where you are aiming |
| `/gm wave <type or mix> [per wave] [waves] [seconds]` | a wave spawner where you are aiming; each wave hunts the nearest enemies. `/gm wave stop` ends them |
| `/gm goto [type]`, `/gm hunt`, `/gm hold` | send every NPC (or one type) to your crosshair, set them hunting, or hold them where they are |
| `/gm alert <1-5> [reason]`, `/gm announce <text>` | the ship's alert level and public address |
| `/gm become <b1, b2 or commander>`, `/gm become off` | play an event character: its model, health and side (clones see you as an enemy, droids as one of their own). It ends when you step out or die; nothing is saved to your trooper |
| `/gm cleanup` | removes every NPC, wave and wreck |
| `/gm list` | what's alive |

Console: `swrp_npc_spawn <type> [count] [x y z]` (host; in front of the camera without coordinates), `swrp_npc_wave <type|mix>
[per wave] [waves] [seconds]` (host, in front of the camera), `swrp_npc_clear`, `swrp_npc_info`.

## Atmosphere

**Ambience** (`Code/Player/SwrpAmbience.cs`, sounds from `SoundSource/ambience.py`): every area has its own room tone, crossfading
over about two seconds as you walk between them: hangar wind and engine hum, bridge console chirps, corridor air handling,
engineering reactor throb, medbay monitors, temple hush; the brig is kept silent apart from its shields, which hum up
and wind down when switched. Now and then a muffled ship sound plays somewhere behind
the bulkheads (a hatch, cargo, a droid, a far-off garbled PA). Footsteps are metal grate in the hangar and engineering and deck
plating elsewhere, with a light armour rattle; everyone nearby hears them. The areas are boxes in `AMBIENT_ZONES` in
`MapSource/notorious_scene.py`; anywhere else aboard counts as corridor. `swrp_ambient_volume` sets the level.

**Look** (`Code/Player/SwrpPostFx.cs` on the Main Camera, tint drawn by `Code/UI/GradeOverlay.razor`): filmic tonemapping,
gentle bloom, light film grain and a soft vignette, a colour grade per area (cool blue bridge, warm hangar and engineering, red
brig, green medbay), and a slight background blur while aiming down the sights in first person. The alert level pulls the grade
towards red (DEFCON 1 pulses). The grades are the `Grades` table at the top of the file. `swrp_postfx 0` turns the whole stack off.

**Dynamic lighting** (`Code/Lighting/`, Teardown-style): every muzzle flash, bolt, impact, detonator, lightsaber and hull breach asks
`Lighting` for light, and `LightingSystem` gives the best-scoring requests (brightness, closeness to your camera, in front of you,
priority) one of a pooled set of real lights each frame: 20 at most, 2 with shadows (sabers and blasts ask first), the rest simply
not drawn, so a firefight costs what one shot does. The brightest also throw a dim second light off the surface they hit, tinted
by the surface (cool steel grey on the ship, a tinted model's own colour); hits leave a small orange ember that lingers as the
spot cools. Light zones (`LightZone`,
`SwrpLightZone`, `Lighting.AddZone`) make rooms dark (ambient multiplier, capped auto-exposure, the room's own lamps dimmed) and hazy (volumetric fog the lights
glow in), applied on top of the DEFCON tint and the space mood. API: `Lighting.Flash( pos, color, intensity, radius, duration, curve )`,
`Lighting.Attach( go, LightProfile )` (returns a `LightEmitter` to change or `Stop()`), `Lighting.MuzzleFlash`, `Lighting.Impact`,
`Lighting.Explosion`, `Lighting.Bolt`; presets in `LightProfile` (Muzzle, Bolt, Impact, Ember, Saber, Blast,
Sparks, Breach); curves Flash, Linear, Smooth, Ember, Hold, Swell. `swrp_light 0` falls back to the old per-effect lights.

## Comms: the comlink and radio voice

**Voice.** Everyone talks through a clone helmet: nearby voices get a light helmet filter and carry as far as the
speaker's range (B: whisper, normal, yell). **Y** puts your voice on the radio: while it's on, what you say (hold V) goes
out on your radio net (your `/channel`: squad, legion, command for Lieutenants and up, or open) to everyone on that net
whose comms are on (**N**), wherever they are on the ship, through a crackly, band-limited sci-fi radio filter with a
squelch chirp when a transmission starts and ends. People standing near you still hear you through your helmet. Each
listener's own machine decides how to play each voice, so nothing extra goes over the network. Code:
`Code/Player/SwrpComms.cs` (the "Radio" and "Helmet" mixers under Voice, `RadioVoiceProcessor`, who's on which net).

**The comlink** (right of the screen, `Code/UI/ComlinkPanel.razor`) opens and closes with **`/coms`** in chat (also
`/comms`, `/comlink`; remembered between sessions, `swrp_comlink` is the setting). It shows your net and its frequency, your receiver,
whether your voice is on the radio or just your helmet, your range, who is transmitting right now with a live
waveform, and the last few radio and dispatch messages (`/r`, `/dispatch`), which then leave the chat box (with the
comlink closed they stay in chat). It shrinks to its header when the net is quiet; Tab shows it all.

## Bacta pods

Four bacta pods stand round the medbay (`MEDBAY_PODS` in the scene script, `Code/Map/SwrpBactaPod.cs`). E on a free pod:
the glass sinks into the floor, you step in, it rises again, bacta floods the tank and you float in it while it heals you
completely and stops any bleeding (the screen goes green and sound goes muffled from inside), then it drains, the glass
drops and you step out in front of it, about 20 seconds in all. You can look around but not move or shoot while inside;
everyone sees the same animation. One trooper per pod. `swrp_pod_info` lists the pods, `swrp_pod_demo` plays the
animation on every pod for your view. Models from `ModelSource/bacta_pod.py`, sounds from `SoundSource/ship_life.py`.

## Armory

The armory (deck -818, between the armory doors) is stocked: two wall racks of DC-15A rifles and two blaster-cell
crates. E on a rack takes a rifle into your inventory (you can carry two from the racks; each rack holds five and
restocks one every 90 seconds, and everyone sees the rack empty as rifles are taken). E on a crate takes six blaster
cells, once every 20 seconds. The host checks who you are, how far away you are and what's left, and saves your
inventory. Racks and crates are placed by `ARMORY` in `MapSource/notorious_scene.py` and find their own wall; the models
are original (`ModelSource/armory.py`). `swrp_armory_info` lists them and their stock.

## Cantina club and DJ booth

E on the DJ desk in the cantina (deck -818, the booth at the north end of the dance floor) opens the decks: two decks
with play/pause, skip 10 s, restart and eject, a crossfader with timed mixes (4/8/16 s) or cuts, a queue with auto-mix
(the next track loads on the idle deck and fades in as the live one ends), the PA level, and the club FX (moving lights,
lasers, strobe, haze, the booth screen with a spectrum visualiser, four colour palettes). Anyone at the booth can DJ; staff
can lock it. Music and lights are only heard and seen inside the cantina, and everyone there hears the same point of the
track (the host keeps the start time, every machine seeks to it).

Sources: four original built-in loops (`SoundSource/club_music.py`), audio files you own dropped in
`Assets/sounds/music/club/` (every listener needs the same file), or a pasted URL: a direct .mp3/.ogg/.opus/.wav link or an
internet radio stream. YouTube doesn't work: YouTube's terms forbid pulling a video's audio onto the PA, and its player won't run in the game's built-in browser (tested 2026-10-12: the embed shows "Video player configuration error", the full watch page never starts the video), so the booth turns YouTube links away with that explanation. `swrp_club_youtube 1` lets them through again to retry after an engine update. Only play music you have the rights to. Code: `Code/Club/`.

## HUD

The HUD fades what you don't need (`swrp_hud_fade`, on by default): the ammo readout shows when you fire, reload,
draw or switch weapons, or run low; the vitals show when you're hurt, out of breath, bleeding or down; your name, unit
and credits show for a few seconds after you deploy or your credits change. Hold Tab to see everything. `swrp_visor 1`
adds the inside of a clone helmet round the edge of the screen (the T-shaped visor, a faint glass rim and scanlines)
in first person. F8 (or `swrp_hud 0`) hides the whole HUD and the nametags for screenshots; the colour grade stays.
Code: `Code/UI/SwrpHud.razor`, `Code/UI/VisorOverlay.razor` (on the Screen FX panel, with `GradeOverlay`).

## The living ship

Placed by `LIVING_*` in `MapSource/notorious_scene.py`; the code is `Code/Map/SwrpLivingShip.cs` and `SwrpCrew.cs`.
A few failing ceiling lights flicker (engineering, the contrafuge room, the brig, the barracks corridor) and the engine
room's lights throb with the reactor. Steam vents trickle and now and then burst with a hiss, damaged wall panels spit
sparks with a crackle and a flash, and animated wall screens (a status readout, a radar sweep, a power graph) turn red at
DEFCON 3 and below and show HYPERSPACE during a jump. The ambient crew are clones in grey navy fatigues at the bridge pits,
walking the bridge walkways, in the engine room and the medbay, astromechs pottering about the hangar and the engine room,
and mouse droids scurrying along the hangar and the engineering corridor. Each piece finds its own wall, light or floor
when it starts, so a placement only needs a point in the room and a direction to look. Walking crew and droids follow
navmesh paths between their route's points (built in the background the first time they come near you), so they keep
about 16 units clear of walls, consoles and parked ships; standing droids are moved onto the navmesh too. `swrp_crew_info`
shows each one's route state.

All of it is cosmetic: nothing is solid or networked, it runs only near your camera (crew within about 3200 units,
screens 1800), and `swrp_ship_life 0` turns it off. The astromech and mouse droid are original models from
`ModelSource/crew_droids.py`, which also writes the navy crewman (`models/crew/crewman_navy.vmdl`, the clone trooper with
greyed textures). Steam is `materials/fx/steam.png` from `ModelSource/fx_textures.py`; the sounds come from
`SoundSource/ship_life.py`.

## Space and hyperspace

Outside the ship (`Code/Map/SwrpSpace.cs` on the Space object) is an original starfield with a faint galaxy band and a
nebula, and a planet ahead of the bow that fills the bridge windows, with its moon in the -X hangar bay's opening. The
skies are painted by `MapSource/space_sky.py` into `Assets/materials/space/`. Now and then a pair or trio of fighters, a
shuttle or a frigate (`ModelSource/starships.py`) flies down one side of the ship or across the bow.

Game Masters jump the ship with **`/hyperspace <destination> [seconds]`** (alias `/jump`; destinations `coruscant`,
`kamino`, `geonosis`, `christophsis`, or 1-4; 4-60 seconds in the tunnel, 12 by default). Everyone aboard gets the same
sequence: a ship announcement and alarm chime, six seconds of the hyperdrive spooling up while the ship shudders harder,
a flash as the stars stretch into streaks, the blue tunnel (it turns slowly, streaks rush past every viewport and the
hull rumbles), then a flash and streaks again as the ship drops out at the new planet, and an arrival announcement.
`/hyperspace` on its own says where the ship is. The planet is kept by the host, so players who join later see it.
Sounds are synthesized by `SoundSource/space_sounds.py` into `Assets/sounds/space/`.

## Tatooine

When the ship is over Tatooine (`/hyperspace tatooine`), the parked **LAAT/i shuttle** in the main hangar (it doesn't fly)
takes troopers down: look at it and press **E**, the screen dips to black over the gunship ride and you step out on the
landing field. The LAAT/i parked there brings you back up to the hangar at any time, even after the ship has jumped away.
The surface (`Code/Map/Tatooine/`) is an original desert on an s&box Terrain, authored offline by `MapSource/tatooine_terrain.py`
(modelled on the Tunisian film locations: an eroded escarpment like Ong Jemel, crescent and linear dunes, a pale basin) with a
small town south of the landing field (cantina, domed houses, a ksar, market; `ModelSource/tatooine_town.py`, layout in
`MapSource/tatooine_layout.py`) and a Separatist outpost to the north-east that Game Masters open with **`/gm outpost`** (shows it
and spawns its droid garrison; `/gm outpost off` hides it). Procedural sand/rock/hardpan textures, twin suns that cross the sky
through a day and night cycle, sandstone boulders and spires (`ModelSource/tatooine_rocks.py`), desert haze, gusts of wind that
throw sand past the camera, sand footsteps and dust kicked up under every trooper's and NPC's feet. The wind is recorded (`SoundSource/tatooine_ext_sounds.py`, from a local GMod
addon: local only, never publish), with a synthesized fallback (`SoundSource/tatooine_sounds.py`).

| Command | What it does |
|---|---|
| `/time` | Tatooine's time of day. Game Masters: `/time <0-24, dawn, noon, dusk, night> [freeze]`, `/time speed <minutes per day>`, `/time run` |
| `swrp_tatooine_time <hour> [1]` | Host: same as `/time` (1 freezes) |
| `swrp_tatooine -1/1/0` | Your machine: load the surface when needed (default), always, never |
| `swrp_tatooine_sunlight`, `swrp_tatooine_haze`, `swrp_tatooine_wind`, `swrp_tatooine_wind_volume` | Your view: sunlight strength, haze strength, wind and blowing sand on/off, wind volume (default 0.4) |
| `swrp_tatooine_info` | Print the surface's state |

## Sounds

All UI and weapon-handling sounds are original, synthesized by `SoundSource/ui_sounds.py` (run it with
`blender -b --factory-startup -P SoundSource/ui_sounds.py`; Blender ships numpy). The WAVs and their `.sound` events are in
`Assets/sounds/ui/`: edit a `.sound` to change volume or pitch, or the recipe in the script to change the sound itself.
`Code/Player/SwrpAudio.cs` lists them, and `swrp_uisounds 0` turns them off.

hover, click, confirm, deny (menu buttons, via `sound-in` rules at the end of `CharacterMenu.razor.scss` and `InventoryPanel.razor.scss`),
open/close (inventory, F4 menu, chat), chat message ping, item use, weapon draw and holster (heard by nearby players too),
door open, close, lock and unlock, the hangar bay alarm, the turbolift hum and arrival chime, the teleport whoosh, the alert
klaxon and alert-level change tone, the ship announcement chime, and the thermal detonator's arming chirp, fuse beeps and
blast (a crack, a sub thump, a rolling roar, falling debris and a long rumble).

**Credit:** the character screen's hover, click, confirm (ENLIST, DEPLOY) and deny sounds (`Assets/sounds/ui/charmenu_*`)
are from Kenney's *Interface Sounds* pack (www.kenney.nl, https://kenney.nl/assets/interface-sounds), licence CC0 1.0
(public domain; credit not required but given). `SoundSource/kenney_ui_sounds.py` converts the picked files from the pack.

## HD rebuild (2026-10-09)

The voxel game is gone: the ship, troopers, droids, items and craft are high-detail PBR models again (backup of everything
before: `X:\SWRP Projects\_backups\swrp-before-hd-20261009-1230.zip`; the voxel era's own backup is
`swrp-before-voxel-20261007-0122.zip`).
- **The ship** is the Notorious port again (`MapSource/notorious_build.py`, see Map below). Its corridors are rebuilt with
  the Venator corridor kit from `X:\SWRP Projects\Corridor Prefab` (`MapSource/notorious_corridors.py`): corridors are
  found on the deck plan, measured with rays, and laid with the kit's wall panels (fitted to each wall run, stretched a
  little rather than leaving gaps), pillars and bulkhead frames, door frames in the doorways and ceiling light panels;
  the map's own faces there are cut away and the floor gets the kit's floor texture. `MapSource/corridors_only.py` re-lays
  just the corridors; `MapSource/preview_render.py` renders an offline look at any spot.
- **Troopers and Jedi** are Battlefront II models (`X:\SWRP Projects\BF2_Extracted`, local only) on **one** Citizen rig
  (`ModelSource/bf2_clones.py`): Trooper, Heavy, Specialist, Officer and the Jedi (Kenobi or Skywalker head, from the
  character's face). Battalions are material groups painted from BF2's own unit markings (`ModelSource/bf2_markings.py`;
  501st, 212th, 104th, 327th, 41st, Coruscant Guard), worn by `Code/Characters/SwrpOutfit.cs`. `ModelSource/bf2_clone_vmdl.py`
  writes the vmdls (`Assets/models/troopers`). A clone stands 72 units (1.83 m): `swrp_player_scale` is 1 by default.
- **Blasters, hilts and items**: `ModelSource/bf2_props.py` (BF2 DC-15A, DC-17, DC-17m, DC-15, Z-6, E-5; the hero hilts; the
  thermal detonator and stun baton) and `ModelSource/hd_props.py` (original small props and the menu plinth);
  `Code/Data/ItemModels.cs` maps items to models.
- **Craft**: the BF2 LAAT/i and ARC-170 (`ModelSource/bf2_craft.py`, `Assets/models/craft`), flown by `Code/Flight`.
  The main hangar's dorsal roof is a working door (`swrp_roofdoors 0/1`, or the button in the hangar control room) so
  gunships lift straight out and land back on their pads.
- **Lighting**: the dynamic lighting framework (`Code/Lighting`) stays; its zones were re-measured for the PBR ship
  (`LightZones.Defaults`, STYLE_GUIDE section 3).

## Flying (ARC-170 and LAAT/i)

Flyable craft wait on the main hangar deck (one ARC-170, one LAAT/i gunship; a wrecked one is replaced after
`swrp_flight_respawn` seconds, default 90). The design follows Garry's Mod's WAC aircraft, in original code (`Code/Flight`).

- **Boarding**: walk up and press **E** for the nearest free seat (the cockpit at the nose, the turrets, or one of eight
  standing places in the gunship's open bay). **1-9** change seat, **E** gets out (in the air that's a jump).
- **Pilot**: **R** engines on/off (they spool up), the craft turns to your **mouse**. Fighter: **W/S** throttle, **A/D** roll,
  it banks into turns. Gunship: hovers level by itself, **WASD** fly. Both: **Space/Ctrl** up/down, **Shift** boost,
  hold **Alt** to look round without steering, **C** cockpit or chase view.
- **Weapons** (Mouse1/Mouse2 for your seat): ARC-170 pilot - wing laser cannons and 6 proton torpedoes; tail gunner - twin
  aft lasers. LAAT/i pilot - wing laser cannons and 8 mass-driver missiles; co-pilot - chin anti-personnel turret; two
  gunners - the ball turrets. Launchers reload a round at a time.
- **Damage**: blasters, detonators, other craft and crashes hurt the hull; each fifth lost tears plating loose (sparks and
  smoke). At zero the craft loses power, burns, falls and blows up, killing anyone still aboard.
- Host commands: `swrp_ship_spawn <arc170|laat>` (where the camera looks), `swrp_ship_list`, `swrp_ship_repair`,
  `swrp_ship_damage <amount>`, `swrp_ship_fly <forward> <lift> <turn> <seconds> [strafe]` (dev: fly an empty craft),
  `swrp_ship_clear` (pads refill). `swrp_flight 0` (saved) brings back the old static parked ARC-170 instead.

## New players, careers and event nights (2026-10-12)

**Rules and the guide.** The first time someone deploys, the Escape menu opens on the server's **RULES** page and stays there
until they press I ACCEPT (the host never has to). The rules live in `swrp/rules.json` (written with sensible defaults the first
time anyone joins): edit `Title`, `Welcome` and `Lines`, raise `Version` so everyone accepts again, then `swrp_rules_reload`.
**F2** (or `/guide`) opens the **GUIDE** page: six first steps, a DIRECTIONS row and EVERY KEY (`/keys`). `/rules` shows the rules.

**Directions.** `/dir <place>` (or the guide's buttons) puts a marker on your HUD with the distance; when the place is on another
deck it points you to the turbolift (or the teleport pad) first. Places: armory, medbay, lift, sim, bridge, holomap, hangar,
brig, briefing, barracks, or part of any door's name (`/dir cantina`). `/dir off` clears it. `/guide tour` walks a new trooper
round the armory, medbay, a turbolift and the simulation rooms. The deck you're on shows at the top of the screen.
`/where` tells you your deck; officers (2nd Lieutenant+) and staff can `/where <trooper>`.

**Tab** is the mission board and scoreboard together: the active order (and a Game Master's event objective), recent orders,
everyone aboard by battalion with class and ping, platoons and squads, and the Trooper of the Week.

**Cadet training.** Cadets get a checklist when they deploy: chain of command, drill, comms, medical, a simulation course run
(ticks itself) and the ship tour (ticks itself). Training Officers (the `trainer` qualification), Lieutenants and up and staff
run it: `/train list`, `/train check <cadet> <1-6|all>`, `/train uncheck`, `/train note`, `/train fail <cadet> [reason]` and
`/train pass <cadet>` (promotes Cadet to Private, logged in the rank log; `force` skips missing items). `/train` shows your own.

**Qualifications.** Battalion officers (2nd Lieutenant+, or the Commander, XO or Jedi General) and staff `/qual grant|revoke
<trooper> <qual>`: `heavy`, `sniper`, `medic`, `officer`, `trainer`, `arc`, `pilot`, `engineer`. `heavy` unlocks Heavy, `sniper`
Specialist, `medic` Medic and `officer` Officer (the one list is `Qual` in `Code/Data/Classes.cs`) in that battalion (`swrp_class_quals 0` on the host opens every class; admins and the unassigned
shinies' Heavy and Specialist are never locked). New characters always enlist as Troopers (no class choice on the character screen). `/class <id>`
switches your own class once you hold its qualification (redeploys at a spawn point, 30 s cooldown); officers and staff
`/class <trooper> <id>` set it for a trooper and grant its qualification with it; revoking the qualification puts them back to
Trooper. Staff use of `/qual` and `/class` follows the PERMISSIONS grid (default Moderator). `/qual` lists yours.

**Recognition.** Captains and up (or the Commander, XO, Jedi General) and staff `/commend <trooper> <medal> [citation]`: clone_cross,
valor, combat_medic, marksman, pilot_wings, service_star, wound_stripe, leadership; it's announced ship-wide and shows on
`/examine` and `/medals [trooper]`. Commanders, XOs, Majors and up name the Trooper of the Week with `/totw <trooper> [reason]`.
`/stats [me|all|<battalion>]` counts revives, enemy kills, cadets passed and simulation runs. `/loa <days> [reason]` puts you on
leave (the roster shows "LOA to ..."), `/loa end` when you're back, `/loa list [battalion]`. All of it is in `swrp/career.json`.

**Comms.** `/channel pa` (Captain+ and staff) puts your radio voice (Y, then V) on the ship's speakers: everyone hears it, receiver
on or off. Someone on your net within a few metres is heard in person through their helmet rather than over the radio.

**Squads** are saved (`swrp/squads.json`) and keep members who log off; `swrp_squads_persist 0` goes back to session-only squads.

**Event tools (Game Masters, also on the J menu's EVENT rows).**

| Command | What it does |
|---|---|
| `/gm bring <all\|battalion\|squad name>` | brings that group round your crosshair (staff can `/return` them) |
| `/gm eventspawn set [battalion\|all]` / `clear` | deaths and new deploys come back at your crosshair instead of the medbay |
| `/gm objective <text>` / `clear` | the objective on everyone's HUD and the Tab board |
| `/gm timer <minutes> [label]` / `stop` | a countdown under the objective |
| `/gm layout save\|load\|list\|delete <name>` | saves the spawn-menu props and every NPC (type, place, hold or hunt) to `swrp/layouts/<name>.json` |
| `/gm music <theme\|underlevel\|hyperlane\|outer_rim\|kessel\|stop>` | event music for everyone (`swrp_event_music` sets your volume) |
| `/gm become <type> [health]` | play an event character with your own health total (1-5000) |

**Staff names.** A typed staff command that matches more than one player now refuses and lists them; exact names win, and
"quoted names" can have spaces. Jail sentences are saved in `punishments.json`, and a prisoner who respawns, switches character or
reconnects goes straight back to a cell.

**Shots and lag compensation.** Your own shot (flash, sound, bolt, impact) plays the moment you fire; the host still decides
every hit. Bolts scatter with a seed the host reuses, so what you see is where they went. A client's shot carries what its own
screen hit; the host keeps 0.6 s of every trooper's and NPC's positions and, when the target really stood there, traces the same
ray shifted with the target (cover still blocks it), so moving targets get hit as you saw them. `swrp_lagcomp 0` (host) turns it
off, `swrp_lagcomp_debug 1` logs every compensated hit and rejected claim. Code: `Player/SwrpLagComp.cs`, `SwrpBlaster.ShootLocal`.

**Armour customiser.** ARMOUR on the character screen (or `/armor`) opens a side panel; the trooper in the hangar wears your picks
live, and SAVE stores them on the character. One piece per slot, each earned by rank, a battalion post or a qualification
(locked ones say what unlocks them; the host checks again on save and on every deploy, so a demotion takes them off):

| Slot | Pieces (unlocked by) |
|---|---|
| Pauldron | Pauldron and bandolier (2nd Lt+, ARC, posts) |
| Kama | Kama, split (2nd Lt+, ARC, posts); Full kama (Sergeant+, officer candidate, heavy) |
| Webbing | Shoulder strap (Corporal+, pilot, engineer); Heavy kit: bandolier, satchel, pack (Staff Sergeant+, heavy) |
| Helmet gear | Specialist visor (Corporal+, marksman, medic); Macrobinocular visor (Staff Sergeant+, marksman, heavy); Helmet antenna (2nd Lt+, officer candidate) |
| Antenna | Shoulder antenna (Sergeant+, engineer, pilot, trainer) |
| Holster | Thigh holster (Private+) |

Helmet off (customiser or `/helmet` in game) shows the clone's face (Jango Fett's, from BF2); the Heavy's and Specialist's visors
are part of their helmets, so theirs stay on. Every battalion now wears body markings in its colour (BF2 only has the 501st's,
so the others reuse its stripes), and kamas, pauldrons and the heavy kit take the battalion colour. The pieces are BF2 parts built
on the trooper rig and drawn bone-merged onto the body; the trooper models are untouched. Build: `ModelSource/bf2_armor.py`,
`bf2_armor_paint.py` (Blender), then `python ModelSource/bf2_armor_vmdl.py`. Code: `Characters/SwrpArmor.cs`,
`UI/ArmorCustomizer.razor`. Local only, like every Battlefront II asset.

**Location tags (for pointing at a spot to fix).** **F6**, `/loc [note]` or `swrp_loc [note]` takes a numbered tag where you stand
and look: deck and area, position and facing, what the crosshair is on (door name, object, model, the point, distance, surface),
the nearest door and a `swrp_goto` line. It is copied to the clipboard, shown on screen for 8 seconds (so it lands in screenshots),
printed to the console as `[SWRP] LOC` and added to `swrp/locations.md` in the data folder (the last 300). Tell Claude
"loc #12" and it can read the line there. `swrp_goto #12` (or `swrp_goto x y z [yaw] [pitch]`) goes back: the host's trooper is
teleported, and before deploying the camera moves there.

**Load test.** `swrp_loadtest <clones> [seconds]` (host) spawns friendly clone NPCs over the spawn points and logs the frame time
(average, 99th percentile, worst); `swrp_loadtest 0` cleans up. It's a rough stand-in: real clients' voice and network traffic need
a second client.

## Code

- `Code/Data` - factions, ranks, items and the character record. Add legions in `Factions.cs`, items in `Items.cs`.
- `Code/Server` - saving, whitelist, the game manager (lobby + spawning) and the network requests.
- `Code/Player/SwrpPlayer.cs` - the RP identity on the spawned player, inventory use/drop.
- `Code/UI` - character menu, HUD, inventory and nametags.

## Map

The map is a port of **rp_venator_notorious_os.vmf** (a Garry's Mod / Source map). Credits from its README: Bananakin (map),
Goldie (map and textures), Halves (models), 122jimbo (textures), Lord Trilobite (skybox props), Grady (statue), Bishop (posters).
The medbay bacta tanks are Lord Trilobite's bacta tank (model and texture), ported to Garry's Mod by KingPommes (Workshop
"Star Wars - Misc Prop Pack"); same rule: local only.

**Licence: keep this local.** The VMF's README says any edit must be released as a public, open-source Steam Workshop map that
credits all of those authors, and warns of a DMCA otherwise. Do not publish this project (or the map model) anywhere until that
is sorted out. The hyperspace systems and turrets are not open source and were never part of the VMF.

Rebuild with:

```
blender -b --factory-startup -P MapSource/gmod_textures.py       # original GMod VTF/VMT -> Assets/materials/gmod (PNG) + vmats, gmod_materials.json
blender -b --factory-startup -P MapSource/notorious_build.py     # brushes -> Assets/models/notorious, materials, notorious_entities.json
python MapSource/notorious_scene.py                              # rebuilds Assets/scenes/notorious.scene (spawns, lights, jail cells, lifts, pads)
```

What came across: world, func_detail and func_brush brushes (rendered and as collision), the 99 player starts, a clustered set of
260 lights (from about 2,700 in the VMF), the 12 brig cells (jail points) and about 3,070 props (consoles, chairs, light
panels, pipes; read from the addon's Source models by `MapSource/source_mdl.py`, placed by `MapSource/notorious_props.py`, baked into
31 spatial chunks under `models/notorious/props`). Solid props collide with their real Source collision shapes (read from the `.phy` files), and each chunk carries its own collision, so a prop that doesn't load can't leave an invisible wall behind. The props are Halves' and the other
authors' models, so the keep-local rule applies to them too. Not ported: dynamic props, most triggers and entity logic, the 3D
skybox, and the sky. The Jedi temple and the simulation rooms (playable areas far above the ship, reached by teleporter) are
ported; `MapSource/regions.py` says where each area and the skybox are. The blue ray shields that were never solid in Garry's Mod (the hangar openings) stay see-through and have no
collision, so players and, later, ships pass through; `SHIELD_OPACITY` in `notorious_build.py` sets how faint they are. The map's light bars, lamps and screen backlights (its "sky_fake_white" material) glow warm white (`LIGHT_PANEL_GLOW`), window glass is a faint glossy blue pane, and both bridges get extra crew furniture the map left out (pit chairs, a command-deck holo-table and wall screens; `bridge_dressing()` in `notorious_props.py`). Brush faces use the
map's own textures (converted from the workshop addon's VTF/VMT, with Source UV projection, normal maps, translucency and
self-illumination); 86 of the 89 materials in the rendered set matched, and 3 fall back to tinted stand-ins
(BANANAKIN_VARDOS_WALL_EMPTY, SKY_NIGHTGLOOM_04_HDR, WALL_DETAIL_01). The textures belong to the map's authors listed above;
the same keep-local rule applies. The doors, turbolifts and teleport pads work (see their sections above); every deck the lift shafts serve gets its own copy of the lift car, and buttons and fans sit where the VMF put them. Hidden tool faces that props used to cover are filled with a dark wall panel, and displacements are tessellated.

Each Blender run writes the model under a new revision name, registers it with a `.meta` file and rewrites `notorious.vmdl`
(the asset compiler caches FBX imports by file name, and unregistered files make the editor recompile in a loop). Wait ~10 s,
recompile `models/notorious/notorious.vmdl`, then re-assign the Model on the map object to reload it in an open scene.

The old placeholder Venator map was removed; a backup is at `X:\SWRP Projects\_backupsenator_placeholder_map_backup.zip`.

