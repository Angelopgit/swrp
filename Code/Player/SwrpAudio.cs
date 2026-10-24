using System;

namespace Swrp;

/// <summary>
/// Every UI and handling sound the gamemode plays, in one place. The .sound events live in Assets/sounds/ui/ (volume and
/// pitch are edited there); the audio itself is synthesized by SoundSource/ui_sounds.py.
/// </summary>
public static class SwrpAudio
{
	/// <summary>
	/// A weapon's sound: the local-only Workshop sound (Assets/sounds/weapons_ext/&lt;id&gt;.sound, from the TFA Star Wars
	/// shared pack, Angelo's call 2026-10-12; never publish) when that folder is present, else our synthesized one.
	/// </summary>
	public static string Weapon( string id, string synthesized )
	{
		var ext = $"sounds/weapons_ext/{id}.sound";
		return ResourceLibrary.Get<SoundEvent>( ext ) is not null ? ext : synthesized;
	}

	public const string Hover = "sounds/ui/ui_hover.sound";
	public const string Click = "sounds/ui/ui_click.sound";
	public const string Confirm = "sounds/ui/ui_confirm.sound";
	public const string Deny = "sounds/ui/ui_deny.sound";
	public const string Open = "sounds/ui/ui_open.sound";
	public const string Close = "sounds/ui/ui_close.sound";
	public const string ChatOpen = "sounds/ui/ui_chat_open.sound";
	public const string ChatMessage = "sounds/ui/ui_chat_msg.sound";
	public const string UseItem = "sounds/ui/ui_use_item.sound";
	public const string WeaponDraw = "sounds/ui/weapon_draw.sound";
	public const string WeaponHolster = "sounds/ui/weapon_holster.sound";
	public const string FlashlightOn = "sounds/ui/flashlight_on.sound";
	public const string FlashlightOff = "sounds/ui/flashlight_off.sound";
	public const string DoorOpen = "sounds/ui/door_open.sound";
	public const string DoorClose = "sounds/ui/door_close.sound";
	public const string DoorLock = "sounds/ui/door_lock.sound";
	public const string DoorUnlock = "sounds/ui/door_unlock.sound";
	public const string HangarAlarm = "sounds/ui/hangar_alarm.sound";
	public static string BlasterStun => Weapon( "stun", "sounds/ui/blaster_stun.sound" );
	public const string BlasterReload = "sounds/ui/blaster_reload.sound";
	public const string BlasterEmpty = "sounds/ui/blaster_empty.sound";
	public const string BlasterImpact = "sounds/ui/blaster_impact.sound";
	public const string HitMarker = "sounds/ui/hit_marker.sound";
	public const string HitMarkerKill = "sounds/ui/hit_marker_kill.sound";
	public const string Hurt = "sounds/ui/hurt.sound";
	public const string RadioStatic = "sounds/ui/radio_static.sound";
	public const string Dispatch = "sounds/ui/dispatch.sound";
	public const string OrderPosted = "sounds/ui/order_posted.sound";
	public const string SquadJoin = "sounds/ui/squad_join.sound";
	public const string Heartbeat = "sounds/ui/heartbeat.sound";
	public const string Bleed = "sounds/ui/bleed.sound";
	public const string ReviveStart = "sounds/ui/revive_start.sound";
	public const string ReviveDone = "sounds/ui/revive_done.sound";
	public const string BactaStation = "sounds/ui/bacta_station.sound";
	public const string LiftRide = "sounds/ui/lift_ride.sound";
	public const string LiftArrive = "sounds/ui/lift_arrive.sound";
	public const string Teleport = "sounds/ui/teleport.sound";
	public const string AlertKlaxon = "sounds/ui/alert_klaxon.sound";
	public const string AlertChange = "sounds/ui/alert_change.sound";
	public const string ShipChime = "sounds/ui/ship_chime.sound";
	public const string WeaponDrop = "sounds/ui/weapon_drop.sound";
	public const string EarRing = "sounds/ui/ear_ring.sound";
	public const string RadioOpen = "sounds/ui/radio_open.sound";
	public const string ShieldOn = "sounds/ui/shield_on.sound";
	public const string ReloadEject = "sounds/ui/reload_eject.sound";
	public const string ReloadInsert = "sounds/ui/reload_insert.sound";
	public const string ReloadCharge = "sounds/ui/reload_charge.sound";
	public const string ShieldOff = "sounds/ui/shield_off.sound";
	public const string RadioClose = "sounds/ui/radio_close.sound";
	public const string CommsOn = "sounds/ui/comms_on.sound";
	public const string CommsOff = "sounds/ui/comms_off.sound";
	public const string WeaponPickup = "sounds/ui/weapon_pickup.sound";
	public const string DetonatorArm = "sounds/ui/detonator_arm.sound";
	public const string DetonatorBeep = "sounds/ui/detonator_beep.sound";
	public static string DetonatorBlast => Weapon( "detonator_blast", "sounds/ui/detonator_blast.sound" );      // the TFA pack blast when present (local only)
	public const string DetonatorBoom = "sounds/ui/detonator_blast.sound";     // our synthesized boom, layered under it for weight

	/// <summary> Set false to silence everything from code (console: swrp_uisounds 0). </summary>
	[ConVar( "swrp_uisounds", Help = "Play the sci-fi UI and weapon handling sounds (1 = on, 0 = off)", Saved = true )]
	public static bool Enabled { get; set; } = true;

	/// <summary> A flat interface sound, only this player hears it. </summary>
	public static void Ui( string sound )
	{
		if ( !Enabled || !Game.IsPlaying ) return;
		Sound.Play( sound );
	}

	/// <summary> A sound in the world, so nearby players hear a trooper draw or holster. </summary>
	public static void At( string sound, Vector3 position )
	{
		if ( !Enabled || !Game.IsPlaying ) return;
		Sound.Play( sound, position );
	}
}
