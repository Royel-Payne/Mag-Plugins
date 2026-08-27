using Mag_SuitBuilder.Search;

using MagSuitBuilderWeb.Models;

namespace MagSuitBuilderWeb.Services;

/// <summary>
/// Bounded, ranked store of completed suits for one search. The comparator matches the WinForms
/// results tree exactly (Form1.CompareSuits): piece count desc, effective legendaries desc,
/// effective epics desc, FEWEST set tinks, base AL desc, then insertion order. Set transfers
/// consume the donor piece, so they rank above AL polish: a transfer has to buy pieces or
/// cantrips to beat a transfer-free suit, never armor level alone.
/// Parent links mirror Form1.FindDeepestNode's superset nesting.
/// </summary>
internal sealed class SuitStore
{
	public const int Capacity = 200;

	public sealed record Entry(int SuitId, int? ParentSuitId, bool IsBaseSuit, CompletedSuit Suit, SuitDto Dto, long Order);

	readonly object gate = new();
	readonly List<Entry> entries = new();
	int nextSuitId = 1;
	long nextOrder;

	public int Count { get { lock (gate) return entries.Count; } }

	static int Compare(Entry a, Entry b)
	{
		int result = b.Dto.Count.CompareTo(a.Dto.Count);
		if (result != 0) return result;
		result = b.Dto.TotalEffectiveLegendaries.CompareTo(a.Dto.TotalEffectiveLegendaries);
		if (result != 0) return result;
		result = b.Dto.TotalEffectiveEpics.CompareTo(a.Dto.TotalEffectiveEpics);
		if (result != 0) return result;
		result = a.Dto.TotalSetTinkers.CompareTo(b.Dto.TotalSetTinkers);
		if (result != 0) return result;
		result = b.Dto.TotalBaseArmorLevel.CompareTo(a.Dto.TotalBaseArmorLevel);
		if (result != 0) return result;
		return a.Order.CompareTo(b.Order);
	}

	/// <summary>
	/// Adds a suit; assigns id and parent, evicts the comparator-worst entry when over capacity.
	/// Returns null when the incoming suit itself would be the evicted one (i.e. it ranks below
	/// every stored suit and the store is full).
	/// </summary>
	public (Entry Added, Entry Evicted)? TryAdd(CompletedSuit suit, Func<int, int?, bool, SuitDto> dtoFactory, bool isBaseSuit)
	{
		lock (gate)
		{
			// Superset nesting exactly like FindDeepestNode: walk down through stored suits the
			// new suit strictly contains.
			int? parentId = null;
			bool descended = true;
			while (descended)
			{
				descended = false;
				foreach (var e in entries)
				{
					if (!Equals(e.ParentSuitId, parentId))
						continue;
					if (suit.IsProperSupersetOf(e.Suit))
					{
						parentId = e.SuitId;
						descended = true;
						break;
					}
				}
			}

			int suitId = nextSuitId++;
			var dto = dtoFactory(suitId, parentId, isBaseSuit);
			var entry = new Entry(suitId, parentId, isBaseSuit, suit, dto, nextOrder++);

			if (entries.Count >= Capacity)
			{
				// Find the comparator-worst evictable entry (base suit exempt)
				Entry worst = null;
				foreach (var e in entries)
				{
					if (e.IsBaseSuit)
						continue;
					if (worst == null || Compare(e, worst) > 0)
						worst = e;
				}

				if (worst != null && Compare(entry, worst) >= 0)
					return null; // incoming suit ranks at/below the worst stored one — drop it

				if (worst != null)
				{
					entries.Remove(worst);

					// Re-parent evicted entry's children
					for (int i = 0; i < entries.Count; i++)
					{
						if (entries[i].ParentSuitId == worst.SuitId)
							entries[i] = entries[i] with { ParentSuitId = worst.ParentSuitId, Dto = entries[i].Dto with { ParentSuitId = worst.ParentSuitId } };
					}

					entries.Add(entry);
					return (entry, worst);
				}
			}

			entries.Add(entry);
			return (entry, null);
		}
	}

	public IReadOnlyList<Entry> RankedSnapshot(int top)
	{
		lock (gate)
		{
			var sorted = new List<Entry>(entries);
			sorted.Sort(Compare);
			return sorted.Take(top).ToList();
		}
	}

	public Entry Get(int suitId)
	{
		lock (gate)
			return entries.FirstOrDefault(e => e.SuitId == suitId);
	}
}
