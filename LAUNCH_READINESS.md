# SWRP launch readiness (2026-10-21)

Read-only review of `Code/` (48.7k lines) and `Assets/`. No code was changed, nothing was hotloaded, and the editor wasn't
touched (the sbox MCP was down this session). Asset licences are in `LICENCE_AUDIT.md`.

## 1. Multiplayer

### What's already solid
- **Host RPCs check who's calling.** All 105 `[Rpc.Host]` methods check `Rpc.Caller` or go through a helper that does
  (`IsCallerOwner()` for Jedi, `SwrpBuild.May()` for build tools, which is staff/GM only).
- **Host-to-client broadcasts are locked down.** 70 of 71 `[Rpc.Broadcast]` are `NetFlags.HostOnly`. The odd one out is
  the gunner aim share in `Flight/SwrpFlightShip.Parts.cs:149`, which only affects visuals.
- **Shared state is host-written.** 164 of 187 `[Sync]` fields are `SyncFlags.FromHost`.
- **Staff limits exist.** Staff commands are rate-limited (`SwrpStaffTools.cs:90`), as are battalion actions. Dropped
  items expire after 10 min and corpses after 75 s. NPCs are capped by `swrp_npc_max` (30).

### Fix before a public server
1. **A client can teleport, holster or knock back other players (high).** These 13 `[Rpc.Owner]` methods have no
   `NetFlags.HostOnly`, so a tampered client can call them on anyone:
   - `SwrpPlayer.TeleportTo` and `SetSpectate`
   - `SwrpBlaster.ForceHolster`, `OfferPickedUp` and `ConfirmHit`
   - `SwrpVitals.NotifyHurt`
   - `JediCombat.ReceiveKnock`, `ReceiveDisarm`, `ReceiveFlash`, `ReceiveCooldown` and `ReceiveNote`

   The fix is to add `NetFlags.HostOnly` to each one. Three dev helpers call `TeleportTo` locally (`SwrpLocTag.cs:164`,
   `SwrpGameManager.cs:437`, `SwrpAimSweep.cs:57`); these are host-only console tools, so they still work on the host.
2. **Chat has no flood limit (medium).** `SwrpChat.SendToHost` caps a line at 220 characters but not how many lines a
   player can send. One client can flood every screen and the chat log. The fix is a per-player limit, for example
   5 lines per 3 s, with staff exempt. This is host-side, in `SwrpChat.cs`; the Physgun thread is fixing the separate
   Enter bug in `ChatPanel`.
3. **Some client-owned values can be faked (low; acceptable for a whitelisted RP server).** The 23 owner `[Sync]` fields
   include `Stamina`, `Sprinting`, `Prone`, `HeldItem`, flight `Heat` and `Overheated`, so a cheater could get infinite
   stamina or no overheat. Movement is also client-owned, as in every s&box game. Shots already go through
   `SwrpLagComp.Plausible`.
4. **There's no network culling (medium at 64 players).** Nothing uses network visibility, so every client receives every
   player, NPC, prop and craft everywhere, including Tatooine while they're on the ship. This needs measuring first (see
   the load test) before deciding whether it needs work.
5. **Build props have no count cap (low).** Building is staff/GM only, but a prop cap per person (say 200) protects the
   server from a mistake.

### Load test (never done)
Every feature has only been played host-side. The proposed run, which needs your go because it drives the editor:
- Start the editor's extra local client instances, 2 at first, then 4. The extra clients use the Test Dummy, never Cub.
- Run the client paths one by one: character create and deploy, chat and radio, doors, lifts, shooting and hit
  feedback, Jedi, flight with a gunner, the club, build tools, and staff and GM commands.
- Watch `[PERF]` frame times on host and clients, plus network bytes per second, with 30 NPCs out.
- A real 64-player test needs real people. Local instances can only show the client bugs and the per-client cost.

## 2. Staff tools (what exists vs what's missing)

| | Today | Proposal |
|---|---|---|
| Roles | User, Moderator, Admin (the host is always Admin), plus a separate GameMaster flag | Add **Trial Moderator** (kick, mute, gag, freeze, goto, bring, return, spectate) and **Senior Admin** (the only role that can `/setrole` admins) |
| Permissions | Fixed in code (`Commands` in `SwrpStaff.cs:203`, `/noclip`, `/god` and others in `SwrpStaffTools.cs`) | A **Permissions** tab in the staff panel: a role × command checkbox grid saved in `swrp/permissions.json`, Senior Admin only. The code table becomes the default |
| Staff chat | Host-to-staff log lines only; staff can't type to each other | `/sc <text>` (and `@` at the start of a line) sends to all online staff; it shows in its own colour and goes into the staff log |
| GM menu (J) | Exists (`UI/GmPanel.razor`) | Plan to check it against the `/gm` command list once it's playable, and add missing buttons there |
| Hotkeys | 26 actions in `Input.config`, plus 10 keys fixed in code: F2 guide, F3 squad, F6 loc tag, F7, F8 HUD, Z prone, U undo, C, Ctrl | Move the fixed keys into `Input.config` so players can rebind them, and add a binds list to the Settings page. F1 is never used, and F7 and F8 keep their current uses |

Because static tables survive a hotload, the role and permissions change needs an editor restart to test.

## 3. Download size

Today: **2.0 GB compiled** (4.8 GB on disk with source files). The ripped content is 1.6 GB of that and goes regardless.
The shippable original set is **438 MB**, and most of it is avoidable:

| Item | Now | Could be |
|---|---:|---:|
| 13 sky textures, 4096×2048 uncompressed RGBA (`materials/space`, about 37 MB each) | 358 MB | about 50 MB with block compression, at almost no visible cost on a sky |
| Tatooine town model (`models/tatooine/town_2_1_0`) | 28 MB | worth checking for duplicate or oversized textures |
| Tatooine `terrain.bin` (raw heightmap) | 25 MB | about 8 MB at 16-bit, or compressed |
| Everything else original | about 27 MB | — |

The replacement art from the licence audit will add to this. A clean first release of about 150-250 MB is realistic.

## Decisions needed
1. Go ahead with the two security fixes (owner RPCs and chat flood)? They hotload, so I'll do them while you're not playing.
2. Go ahead with the load test? It needs the editor, and I'll ask first.
3. Is the staff plan above right? Specifically the two new roles, `/sc` for staff chat, and the permissions grid.
4. Compress the skies now? This is safe and reversible, and the original PNGs stay.
