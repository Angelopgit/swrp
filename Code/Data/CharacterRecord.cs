using System;
using System.Text.Json.Serialization;

namespace Swrp;

/// <summary>
/// A persistent roleplay character. One Steam account owns several of these.
/// Stored on the host in <c>swrp/characters/{steamid}.json</c>.
/// </summary>
public sealed class CharacterRecord
{
	public string Id { get; set; } = Guid.NewGuid().ToString( "N" );
	public ulong OwnerSteamId { get; set; }
	public string Name { get; set; }

	/// <summary> Clone designation number, e.g. "5555" for CT-5555. </summary>
	public string Designation { get; set; }
	public string FactionId { get; set; } = FactionRegistry.CloneTrooper;
	public string ClassId { get; set; } = ClassRegistry.Trooper;

	/// <summary> What other people see when they examine this character. Staff can edit it. </summary>
	public string Description { get; set; } = "";
	public int RankIndex { get; set; }

	/// <summary> A Jedi's looks (Appearance text: body, face, skin, hair, eyes, robes); clones leave it empty. </summary>
	public string Appearance { get; set; } = "";

	/// <summary> Armour customiser pieces (SwrpArmorRegistry part ids, comma separated); the host re-checks them on every deploy. </summary>
	public string Armor { get; set; } = "";

	/// <summary> Deploys with the helmet off (/helmet toggles it in game). </summary>
	public bool HelmetOff { get; set; }

	/// <summary> A role in the battalion set by its officers ("Medic", "ARC Trooper", "Squad Lead"); empty for none. </summary>
	public string BattalionRole { get; set; }
	public int Credits { get; set; } = 100;
	public List<ItemStack> Inventory { get; set; } = new();

	/// <summary> Rounds left in each weapon's magazine, by item id, so a half-empty magazine stays half-empty. </summary>
	public Dictionary<string, int> WeaponClips { get; set; } = new();

	/// <summary> The class whose weapons were last issued (SwrpBlaster.IssueClassWeapons): they come once per class, not every spawn. </summary>
	public string IssuedClass { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public DateTime LastPlayed { get; set; } = DateTime.UtcNow;

	[JsonIgnore]
	public FactionDefinition Faction => FactionRegistry.Get( FactionId ) ?? FactionRegistry.Default;
	[JsonIgnore]
	public RankDefinition Rank => Faction.GetRank( RankIndex );

	/// <summary> e.g. "SGT CT-5555 'Hardcase'" </summary>
	[JsonIgnore]
	public string FullTitle => Faction.TitleFor( Rank, Designation, Name );

	/// <summary>
	/// Adds items, filling existing stacks first. Returns how many could not fit.
	/// </summary>
	public int AddItem( string itemId, int count )
	{
		var def = ItemRegistry.Get( itemId );
		if ( def is null || count <= 0 ) return count;

		foreach ( var stack in Inventory.Where( x => x.ItemId == itemId && x.Count < def.MaxStack ) )
		{
			var add = Math.Min( count, def.MaxStack - stack.Count );
			stack.Count += add;
			count -= add;
			if ( count == 0 ) return 0;
		}

		while ( count > 0 && Inventory.Count < ItemRegistry.InventorySlots )
		{
			var add = Math.Min( count, def.MaxStack );
			Inventory.Add( new ItemStack( itemId, add ) );
			count -= add;
		}

		return count;
	}

	/// <summary>
	/// Removes <paramref name="count"/> from the stack at <paramref name="slot"/>. Returns false if the slot is empty.
	/// </summary>
	public bool TakeFromSlot( int slot, int count = 1 )
	{
		if ( slot < 0 || slot >= Inventory.Count ) return false;

		var stack = Inventory[slot];
		stack.Count -= count;
		if ( stack.Count <= 0 )
			Inventory.RemoveAt( slot );

		return true;
	}
}

/// <summary>
/// What the character select screen needs to know about a character. Sent host to client as json.
/// </summary>
public sealed class CharacterSummary
{
	public string Id { get; set; }
	public string Name { get; set; }
	public string Title { get; set; }
	public string FactionId { get; set; }
	public string ClassId { get; set; }
	public string Appearance { get; set; }
	public string Armor { get; set; }
	public bool HelmetOff { get; set; }
	public string RankName { get; set; }
	public int RankIndex { get; set; }
	public int RankCount { get; set; }
	public int Credits { get; set; }
	public DateTime LastPlayed { get; set; }
	public DateTime CreatedAt { get; set; }
	public int ItemCount { get; set; }

	public static CharacterSummary From( CharacterRecord c ) => new()
	{
		Id = c.Id,
		Name = c.Name,
		Title = c.FullTitle,
		FactionId = c.FactionId,
		ClassId = c.ClassId,
		Appearance = c.Appearance,
		Armor = c.Armor ?? "",
		HelmetOff = c.HelmetOff,
		RankName = c.Rank.Name,
		RankIndex = c.RankIndex,
		RankCount = c.Faction.Ranks.Count,
		Credits = c.Credits,
		LastPlayed = c.LastPlayed,
		CreatedAt = c.CreatedAt,
		ItemCount = c.Inventory.Sum( i => i.Count ),
	};
}

/// <summary>
/// Host to client payload for the character menu.
/// </summary>
public sealed class CharacterMenuState
{
	public List<CharacterSummary> Characters { get; set; } = new();
	public List<string> AllowedFactions { get; set; } = new();
	public int MaxCharacters { get; set; }
	public bool IsAdmin { get; set; }
}
