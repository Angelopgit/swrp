using System;

namespace Swrp.Flight;

/// <summary> How a craft flies. </summary>
public enum FlightModel
{
	/// <summary> A starfighter: throttle sets the speed, the nose follows your aim and banks into turns, repulsors keep it up. </summary>
	Fighter,
	/// <summary> A repulsorlift gunship: hovers level by itself, W/S/A/D move it, Space/Ctrl climb and sink, the nose turns to your aim. </summary>
	Gunship,
}

public enum SeatRole { Pilot, CoPilot, Gunner, Passenger }

public enum WeaponKind
{
	/// <summary> Laser bolts traced instantly (with a travelling bolt for the eye). </summary>
	Laser,
	/// <summary> A projectile that flies in a straight line and blows up (torpedoes, missiles). </summary>
	Missile,
}

/// <summary> One seat: where you sit (ship space: x forward, y left, z up), where your eyes are, what you fire. </summary>
public sealed class SeatDef
{
	public string Name;
	public SeatRole Role;
	public Vector3 Position;
	public float Yaw;                    // which way you face in the ship (passengers at the doors face out)
	public Vector3 Eye;                  // the camera for this seat (cockpit / turret view)
	public bool HideBody = true;         // false: seen by everyone (cockpit seats under the canopy, the open bay)
	public bool Sit;                     // sat down (the cockpit); otherwise standing (the bay)
	public string[] Weapons = Array.Empty<string>();  // Attack1 fires the first, Attack2 the second
	public Vector3 ExitOffset;           // where you step out
}

public enum PartMotion { Slide, Turret }

/// <summary> A moving part: its own model (built round its pivot), the point it moves about, and how it moves. </summary>
public sealed class PartDef
{
	public string Name;
	public PartMotion Motion;
	public string ModelPath;
	public Vector3 Pivot;
	/// <summary> Slide: how far it moves when open. </summary>
	public Vector3 Slide;
	/// <summary> Turret: whose aim it follows, which way it points at rest and how far it may swing. </summary>
	public int Seat = -1;
	public Vector3 Rest = Vector3.Forward;
	public float Arc = 90f;
}

/// <summary> A gun or launcher. Muzzles come in pairs or more; linked guns fire together, others take turns. </summary>
public sealed class WeaponDef
{
	public string Id;
	public string Name;
	public WeaponKind Kind = WeaponKind.Laser;
	public Vector3[] Muzzles;
	public bool Linked;                  // all muzzles at once (else alternating)
	public float FireDelay = 0.2f;
	public float Damage = 40f;           // per bolt to troopers and NPCs
	public float ShipDamage = 60f;       // per bolt to other craft
	public float Range = 14000f;
	public float Spread = 0.6f;          // degrees
	public Color BoltColor = new( 1f, 0.2f, 0.15f );
	public string Sound;

	/// <summary> Fixed guns may be steered this many degrees off the nose to converge on your aim; turrets swing within their arc. </summary>
	public float Gimbal = 6f;
	/// <summary> Turret: the direction it points at rest (ship space) and how far it swings from it. 0 = fixed forward gun. </summary>
	public Vector3 ArcCenter = Vector3.Forward;
	public float Arc;

	// missiles
	public int Ammo;                     // 0 = unlimited
	public float ReloadSeconds = 20f;    // one round back every this many seconds
	public float Speed = 4000f;
	public float BlastRadius = 220f;
}

public sealed class CraftDef
{
	public string Id;
	public string Name;
	public FlightModel Model;
	/// <summary> The hull (ModelSource/bf2_craft.py): ship space, x forward, y left, z up, z = 0 under the landing gear. </summary>
	public string ModelPath;

	public float Mass = 8000f;
	public float Health = 1500f;
	public (Vector3 Center, Vector3 Size)[] Boxes;

	// flying
	public float MaxSpeed = 2500f;       // full throttle (fighter) or full forward (gunship), units/s
	public float BoostMul = 1.5f;
	public float Accel = 1200f;
	public float StrafeSpeed = 700f;     // gunship sideways
	public float ClimbSpeed = 700f;
	public float PitchRate = 80f;        // deg/s
	public float YawRate = 45f;
	public float RollRate = 160f;
	public float MaxBank = 65f;
	/// <summary> How fast a turn winds up and runs out (deg/s^2): the craft's rotational inertia. </summary>
	public float TurnAccel = 160f;
	/// <summary> Gunship: how fast the heading's turn winds up (deg/s^2). </summary>
	public float YawAccel = 60f;
	/// <summary> The afterburner: seconds of it from cold, and seconds to cool from burnt out. </summary>
	public float BoostSeconds = 6f, BoostCool = 6f;
	/// <summary> Engine nozzles (ship space) that glow with power and flare on the afterburner, and their glow size. </summary>
	public Vector3[] EngineGlows = Array.Empty<Vector3>();
	public float EngineGlowSize = 60f;
	/// <summary> Fighter: how hard the airframe kills sideslip (1 = on rails; lower drifts through turns). </summary>
	public float SlipGrip = 1f;
	/// <summary> Where thin vapour trails stream from at speed (ship space): the fighter's wingtips, the gunship's tail. </summary>
	public Vector3[] Contrails = Array.Empty<Vector3>();
	public float SpoolSeconds = 3f;      // engines from cold to full (WAC's engine RPM spin-up)
	public float CameraDistance = 900f;
	public float CameraHeight = 220f;

	public SeatDef[] Seats;
	public WeaponDef[] Weapons;

	// moving parts with their own models (none on the BF2 craft yet: their gear, canopies and doors are part of the hull)
	public string CanopyModel;
	public Vector3 CanopyCenter, CanopyRadii, CanopyHinge;
	public float CanopyOpenAngle = 55f;
	public string GearModel;
	public float GearTop;
	/// <summary> Which of <see cref="Boxes"/> is the landing gear's collision (off while the gear is up). </summary>
	public int GearBox = -1;
	public PartDef[] Parts = Array.Empty<PartDef>();
	/// <summary> A gunship with doors the pilot opens and shuts (key 0). </summary>
	public bool HasDoors => Parts.Any( p => p.Motion == PartMotion.Slide );

	public WeaponDef Weapon( string id ) => Array.Find( Weapons, w => w.Id == id );
	public int PilotSeat => Array.FindIndex( Seats, s => s.Role == SeatRole.Pilot );
}

/// <summary> The craft models (Battlefront II, ModelSource/bf2_craft.py; local only, never publish). </summary>
public static class CraftModels
{
	public const string Laat = "models/craft/laat.vmdl";
	public const string Arc170 = "models/craft/arc170.vmdl";
	public const string VWing = "models/craft/vwing.vmdl";
}

/// <summary>
/// The flyable Republic craft. Loadouts follow the films: the ARC-170's two wing laser cannons, its proton torpedoes and the
/// tail gunner's twin aft lasers (Revenge of the Sith); the LAAT/i's wing laser cannons and mass-driver missiles for the pilot,
/// the chin anti-personnel turrets for the forward gunner and the two ball turrets at the bay's front corners (Attack of
/// the Clones), with room for a platoon standing in the open bay. Positions are in the BF2 models' ship space (bf2_craft.py
/// printed the cockpit glass centres the seats sit under): the LAAT/i is 713 long, 767 across the wings, 260 high; the
/// ARC-170 467 long, 774 across, 156 high.
/// </summary>
public static class CraftRegistry
{
	static readonly Color RepublicRed = new( 1f, 0.22f, 0.16f );
	// the ARC-170's cannons fire blue, as the troopers' blasters do (Angelo, 2026-10-21: "red instead of blue")
	static readonly Color RepublicBlue = new( 0.2f, 0.45f, 1f );
	static readonly Color GunshipGreen = new( 0.3f, 1f, 0.35f );

	public static CraftDef Arc170 => new()
	{
		Id = "arc170",
		Name = "ARC-170",
		Model = FlightModel.Fighter,
		ModelPath = CraftModels.Arc170,
		Mass = 6000f,
		Health = 1200f,
		Boxes = new[]
		{
			( new Vector3( 10, 0, 100 ), new Vector3( 440, 80, 70 ) ),       // fuselage and nose
			( new Vector3( -40, 0, 104 ), new Vector3( 190, 420, 34 ) ),     // inner wings and engines
			( new Vector3( -30, 0, 104 ), new Vector3( 120, 760, 26 ) ),     // the S-foils out to the cannons
			( new Vector3( 80, 0, 104 ), new Vector3( 300, 760, 22 ) ),      // the long wing cannons
			( new Vector3( -190, 0, 140 ), new Vector3( 70, 70, 40 ) ),      // tail
			( new Vector3( 0, 0, 34 ), new Vector3( 280, 160, 68 ) ),        // landing gear
		},
		MaxSpeed = 3200f,
		BoostMul = 1.45f,
		Accel = 1500f,
		ClimbSpeed = 600f,
		PitchRate = 85f,
		YawRate = 40f,
		RollRate = 190f,
		TurnAccel = 240f,              // a light fighter: snaps into and out of turns
		SlipGrip = 0.6f,
		Contrails = new Vector3[] { new( 150, 386, 104 ), new( 150, -386, 104 ) },               // and slides a little through them (Angelo's handling: "crisp, agile, light drift")
		BoostSeconds = 6f,
		BoostCool = 5f,
		SpoolSeconds = 2.5f,
		CameraDistance = 950f,
		CameraHeight = 230f,
		GearBox = 5,
		Seats = new[]
		{
			new SeatDef { Name = "Pilot", Role = SeatRole.Pilot, Position = new( 4, 0, 78 ), Eye = new( 4, 0, 146 ), HideBody = false, Sit = true, Weapons = new[] { "arc_cannons", "arc_torpedoes" }, ExitOffset = new( 0, 160, 0 ) },
			new SeatDef { Name = "Co-pilot", Role = SeatRole.CoPilot, Position = new( -64, 0, 78 ), Eye = new( -64, 0, 146 ), HideBody = false, Sit = true, ExitOffset = new( -60, 160, 0 ) },
			new SeatDef { Name = "Tail gunner", Role = SeatRole.Gunner, Position = new( -150, 0, 100 ), Eye = new( -150, 0, 132 ), Weapons = new[] { "arc_tail" }, ExitOffset = new( -150, 160, 0 ) },
		},
		Weapons = new[]
		{
			new WeaponDef { Id = "arc_cannons", Name = "Medium laser cannons", Muzzles = new Vector3[] { new( 232, 380, 104 ), new( 232, -380, 104 ) },
				FireDelay = 0.16f, Damage = 45f, ShipDamage = 70f, BoltColor = RepublicBlue, Sound = "sounds/flight/arc_cannon.sound", Gimbal = 5f },
			new WeaponDef { Id = "arc_torpedoes", Name = "Proton torpedoes", Kind = WeaponKind.Missile, Muzzles = new Vector3[] { new( 200, 20, 84 ), new( 200, -20, 84 ) },
				FireDelay = 0.9f, Damage = 260f, ShipDamage = 700f, Ammo = 6, ReloadSeconds = 25f, Speed = 4800f, BlastRadius = 300f,
				BoltColor = new Color( 0.45f, 0.65f, 1f ), Sound = "sounds/flight/torpedo.sound", Gimbal = 3f },
			new WeaponDef { Id = "arc_tail", Name = "Twin aft laser cannons", Muzzles = new Vector3[] { new( -232, 4, 128 ), new( -232, -4, 128 ) }, Linked = true,
				FireDelay = 0.22f, Damage = 30f, ShipDamage = 40f, BoltColor = RepublicBlue, Sound = "sounds/flight/bf2/arc_tail.sound",      // BF2's ARC-170 tail gunner (local only)
				ArcCenter = Vector3.Backward, Arc = 70f },
		},
	};

	// LAAT/i landmarks in ship space (bf2_craft.py's glass centres)
	static readonly Vector3 PilotBubble = new( 164, 0, 174 );
	static readonly Vector3 GunnerBubble = new( 256, 0, 139 );
	static readonly Vector3 BallL = new( 132, 139, 48 );
	static readonly Vector3 BallR = new( 132, -139, 48 );
	const float Deck = 22f;              // the troop bay's floor
	const float BayBack = -190f;         // the bay runs from here forward to the cockpit bulkhead

	public static CraftDef Laat => new()
	{
		Id = "laat",
		Name = "LAAT/i gunship",
		Model = FlightModel.Gunship,
		ModelPath = CraftModels.Laat,
		Mass = 14000f,
		Health = 2200f,
		Boxes = new[]
		{
			( new Vector3( -10, 0, 11 ), new Vector3( 640, 172, 22 ) ),       // belly and bay deck
			( new Vector3( -60, 0, 196 ), new Vector3( 470, 172, 20 ) ),      // the roof over the bay and tail
			( new Vector3( 270, 0, 110 ), new Vector3( 150, 150, 200 ) ),     // the nose and cockpits
			( new Vector3( 186, 0, 110 ), new Vector3( 20, 172, 180 ) ),      // the bulkhead at the front of the bay
			( new Vector3( -290, 0, 110 ), new Vector3( 130, 172, 200 ) ),    // the tail section
			( new Vector3( -70, 0, 214 ), new Vector3( 200, 767, 30 ) ),      // the wings
		},
		MaxSpeed = 1700f,
		BoostMul = 1.35f,
		Accel = 700f,
		StrafeSpeed = 650f,
		ClimbSpeed = 650f,
		YawRate = 55f,
		YawAccel = 40f,                // 14 tonnes: the turn takes a moment to build and to stop
		BoostSeconds = 8f,
		BoostCool = 7f,
		Contrails = new Vector3[] { new( -340, 40, 150 ), new( -340, -40, 150 ) },
		SpoolSeconds = 3.5f,
		CameraDistance = 1250f,
		CameraHeight = 330f,
		Seats = Concat(
			new[]
			{
				// the pilot sits behind and above the forward gunner (single file), both seen through their bubbles
				new SeatDef { Name = "Pilot", Role = SeatRole.Pilot, Position = PilotBubble + new Vector3( -4, 0, -40 ), Eye = PilotBubble + new Vector3( 6, 0, 6 ), HideBody = false, Sit = true, Weapons = new[] { "laat_wing", "laat_massdriver" }, ExitOffset = new( 250, 200, 0 ) },
				new SeatDef { Name = "Co-pilot / forward gunner", Role = SeatRole.CoPilot, Position = GunnerBubble + new Vector3( -6, 0, -40 ), Eye = GunnerBubble + new Vector3( 6, 0, 4 ), HideBody = false, Sit = true, Weapons = new[] { "laat_chin", "laat_rockets" }, ExitOffset = new( 300, -200, 0 ) },
				new SeatDef { Name = "Left ball turret", Role = SeatRole.Gunner, Position = BallL + new Vector3( 0, 0, -22 ), Eye = BallL + new Vector3( 4, 0, 6 ), Weapons = new[] { "laat_ball_l" }, ExitOffset = new( 60, 200, 0 ) },
				new SeatDef { Name = "Right ball turret", Role = SeatRole.Gunner, Position = BallR + new Vector3( 0, 0, -22 ), Eye = BallR + new Vector3( 4, 0, 6 ), Weapons = new[] { "laat_ball_r" }, ExitOffset = new( 60, -200, 0 ) },
			},
			BaySeats() ),
		Weapons = new[]
		{
			new WeaponDef { Id = "laat_wing", Name = "Composite-beam wing turrets", Muzzles = new Vector3[] { new( 40, 300, 236 ), new( 40, -300, 236 ) },
				FireDelay = 0.18f, Damage = 42f, ShipDamage = 65f, BoltColor = GunshipGreen, Sound = "sounds/flight/laat_cannon.sound", Arc = 75f },
			new WeaponDef { Id = "laat_massdriver", Name = "Mass-driver missiles", Kind = WeaponKind.Missile, Muzzles = new Vector3[] { new( 120, 40, 214 ), new( 120, -40, 214 ) },
				FireDelay = 0.6f, Damage = 220f, ShipDamage = 600f, Ammo = 6, ReloadSeconds = 18f, Speed = 4200f, BlastRadius = 300f,
				BoltColor = new Color( 1f, 0.6f, 0.3f ), Sound = "sounds/flight/missile.sound", Gimbal = 12f },
			new WeaponDef { Id = "laat_chin", Name = "Chin anti-personnel turrets", Muzzles = new Vector3[] { new( 356, 20, 96 ), new( 356, -20, 96 ) },
				FireDelay = 0.11f, Damage = 26f, ShipDamage = 25f, Spread = 1.2f, BoltColor = GunshipGreen, Sound = "sounds/flight/laat_chin.sound",
				ArcCenter = new Vector3( 1, 0, -0.35f ).Normal, Arc = 70f },
			new WeaponDef { Id = "laat_rockets", Name = "Air-to-air rockets", Kind = WeaponKind.Missile,
				Muzzles = new Vector3[] { new( 20, 140, 206 ), new( 20, -140, 206 ), new( 20, 180, 206 ), new( 20, -180, 206 ), new( 20, 220, 206 ), new( 20, -220, 206 ), new( 20, 260, 206 ), new( 20, -260, 206 ) },
				FireDelay = 0.25f, Damage = 140f, ShipDamage = 380f, Ammo = 8, ReloadSeconds = 12f, Speed = 5200f, BlastRadius = 190f,
				BoltColor = new Color( 1f, 0.75f, 0.4f ), Sound = "sounds/flight/missile.sound", Gimbal = 8f },
			new WeaponDef { Id = "laat_ball_l", Name = "Composite-beam ball turret", Muzzles = new Vector3[] { BallL + new Vector3( 50, 6, 0 ), BallL + new Vector3( 50, -6, 0 ) }, Linked = true,
				FireDelay = 0.24f, Damage = 40f, ShipDamage = 55f, BoltColor = GunshipGreen, Sound = "sounds/flight/turret.sound",
				ArcCenter = new Vector3( 0.6f, 1f, -0.2f ).Normal, Arc = 95f },
			new WeaponDef { Id = "laat_ball_r", Name = "Composite-beam ball turret", Muzzles = new Vector3[] { BallR + new Vector3( 50, 6, 0 ), BallR + new Vector3( 50, -6, 0 ) }, Linked = true,
				FireDelay = 0.24f, Damage = 40f, ShipDamage = 55f, BoltColor = GunshipGreen, Sound = "sounds/flight/turret.sound",
				ArcCenter = new Vector3( 0.6f, -1f, -0.2f ).Normal, Arc = 95f },
		},
	};

	/// <summary>
	/// A full platoon of 30 standing in the open bay: three files of ten, everyone facing forward, one helmet behind the next,
	/// each holding the overhead line (SwrpEmotes.Handhold; Angelo, 2026-10-13). The front of each file fills first.
	/// </summary>
	static SeatDef[] BaySeats()
	{
		var list = new List<SeatDef>();
		foreach ( var row in new[] { 44f, -44f, 0f } )
			for ( var i = 9; i >= 0; i-- )
			{
				var x = BayBack + 22f + i * 34f;
				var side = row >= 0f ? 1f : -1f;
				list.Add( new SeatDef
				{
					Name = row > 0 ? "Troop bay (left file)" : row < 0 ? "Troop bay (right file)" : "Troop bay (centre file)",
					Role = SeatRole.Passenger,
					Position = new( x, row, Deck ),
					Yaw = 0f,
					Eye = new( x, row, Deck + 64f ),
					HideBody = false,
					ExitOffset = new( x, side * 200f, 0f ),
				} );
			}
		return list.ToArray();
	}

	static SeatDef[] Concat( SeatDef[] a, SeatDef[] b ) => a.Concat( b ).ToArray();

	/// <summary> Bump when the craft data changes: a code hotload keeps static fields, so the cached list is rebuilt when this no longer matches. </summary>
	public const int Revision = 9;      // 8: the bay in three forward-facing files; 9: no engine glows, BF2 tail gun sound

	static CraftDef[] _all;
	static int _allRevision;

	public static CraftDef[] All
	{
		get
		{
			if ( _all is null || _allRevision != Revision ) { _all = new[] { Arc170, Laat }; _allRevision = Revision; }
			return _all;
		}
	}

	public static CraftDef Find( string id ) => Array.Find( All, c => c.Id.Equals( id, StringComparison.OrdinalIgnoreCase ) )
		?? Array.Find( All, c => id is not null && ( id.Contains( "arc", StringComparison.OrdinalIgnoreCase ) && c.Id == "arc170" || id.Contains( "laat", StringComparison.OrdinalIgnoreCase ) && c.Id == "laat" || id.Contains( "gunship", StringComparison.OrdinalIgnoreCase ) && c.Id == "laat" ) );
}
