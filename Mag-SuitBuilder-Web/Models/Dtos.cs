namespace MagSuitBuilderWeb.Models;

// ---- Inventory ----

public sealed record SpellDto(int Id, string Name, string CantripLevel);

public sealed record RatingsDto(int Dam, int DamResist, int Crit, int CritResist, int CritDam, int CritDamResist, int HealBoost, int Vitality);

public sealed record ItemDto(
	int ItemKey,
	int GameId,
	string Name,
	string Owner,
	string Server,
	string ObjectClass,
	string EquippableSlots,
	int EquippableSlotsValue,
	string Coverage,
	int CoverageValue,
	string EquippedSlot,
	int ItemSetId,
	string ItemSetName,
	int CalcedStartingArmorLevel,
	int ArmorLevel,
	string Material,
	int Tinks,
	int WieldLevel,
	int SkillLevel,
	RatingsDto Ratings,
	IReadOnlyList<SpellDto> Spells,
	bool Locked,
	bool Excluded,
	string Info);

public sealed record CharacterDto(string Name, IReadOnlyList<ItemDto> Items);

public sealed record ServerDto(string Name, IReadOnlyList<CharacterDto> Characters);

public sealed record ArmorSetDto(int Id, string Name);

public sealed record InventoryDto(
	string RootPath,
	DateTime LoadedAtUtc,
	IReadOnlyList<string> Warnings,
	IReadOnlyList<ArmorSetDto> ArmorSets,
	IReadOnlyList<ServerDto> Servers);

public sealed record LoadRequest(string RootPath);

public sealed record ItemFlagsRequest(bool? Locked, bool? Excluded);

// ---- Cantrips ----

public sealed record CantripLevelDto(int Id, string Name);

public sealed record CantripFamilyDto(
	string Key,
	string Name,
	int Column,
	int Row,
	CantripLevelDto Legendary,
	CantripLevelDto Epic,
	CantripLevelDto Major,
	CantripLevelDto Minor);

public sealed record CantripsDto(
	IReadOnlyList<CantripFamilyDto> Families,
	IReadOnlyDictionary<string, IReadOnlyList<string>> Presets); // preset name -> family keys (legendary level)

// ---- Search ----

public sealed record CharacterRef(string Server, string Character);

public sealed record CantripSelection(string FamilyKey, string Level, int? SpellId);

public sealed record SearchFilters(
	bool RemoveEquipped,
	bool RemoveUnequipped,
	int? MinBaseArmorLevel,
	int? MaxBaseArmorLevel,
	int? MinExtremityArmorLevel,
	int? MaxExtremityArmorLevel,
	bool IncludeBodyArmorClothing,
	bool IncludeShirtsPants,
	bool IncludeJewelry,
	bool JewelryNecklace,
	bool JewelryTrinket,
	bool JewelryBracelet,
	bool JewelryRing,
	int? MinLegendaries,
	int? MaxLegendaries,
	int? MinEpics,
	int? MaxEpics)
{
	public static SearchFilters Default { get; } = new(
		RemoveEquipped: false, RemoveUnequipped: false,
		MinBaseArmorLevel: 0, MaxBaseArmorLevel: 9999,
		MinExtremityArmorLevel: 0, MaxExtremityArmorLevel: 9999,
		IncludeBodyArmorClothing: true, IncludeShirtsPants: true,
		IncludeJewelry: true, JewelryNecklace: true, JewelryTrinket: true,
		JewelryBracelet: true, JewelryRing: true,
		MinLegendaries: 0, MaxLegendaries: 99, MinEpics: 0, MaxEpics: 99);
}

public sealed record SearchRequest(
	IReadOnlyList<CharacterRef> Characters,
	int PrimaryArmorSetId,
	int SecondaryArmorSetId,
	bool AllowSetTransfers,
	IReadOnlyList<CantripSelection> Cantrips,
	SearchFilters Filters);

public sealed record SearchStatusDto(
	Guid SearchId,
	string State,
	double ElapsedSeconds,
	int SuitsFound,
	long ArmorThreads,
	long AccessoryQueued,
	long AccessoryRunning);

// ---- Suits ----

public sealed record DonorDto(int ItemKey, string Name, string Owner, string Info);

public sealed record SuitPieceDto(
	string Slots,
	int SlotsValue,
	int ItemKey,
	string Name,
	string Owner,
	int CalcedStartingArmorLevel,
	int EffectiveSetId,
	string EffectiveSetName,
	int OriginalSetId,
	string OriginalSetName,
	bool IsSetTinkeredVariant,
	DonorDto Donor,
	IReadOnlyList<string> Instructions,
	IReadOnlyList<SpellDto> SearchSpells,
	IReadOnlyList<SpellDto> AllSpells,
	string Info);

public sealed record SetCountDto(int SetId, string Name, int Count);

public sealed record SuitDto(
	int SuitId,
	int? ParentSuitId,
	bool IsBaseSuit,
	int Count,
	int TotalBaseArmorLevel,
	int TotalEffectiveLegendaries,
	int TotalEffectiveEpics,
	int TotalEffectiveMajors,
	int TotalSetTinkers,
	int PrimarySetPieces,   // pieces in the search's chosen primary set, capped at 5 (bonus cap); 0 when primary is Any
	int SecondarySetPieces, // pieces in the chosen secondary set, capped at 4 (the builder's secondary cap); 0 when Any
	string Display,
	IReadOnlyList<SetCountDto> SetCounts,
	IReadOnlyList<SuitPieceDto> Pieces);
