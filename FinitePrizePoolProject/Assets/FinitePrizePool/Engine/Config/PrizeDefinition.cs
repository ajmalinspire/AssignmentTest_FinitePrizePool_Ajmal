using System;
using System.Collections.Generic;
using System.Linq;

namespace FinitePrizePool.Engine.Config
{
    public enum PrizeKind
    {
        Loss,
        Multiplier,
        Bonus
    }

    /// <summary>One prize ticket as defined in a bag, before it is placed in a deal.</summary>
    public readonly struct PrizeDefinition : IEquatable<PrizeDefinition>
    {
        public PrizeKind Kind { get; }
        public decimal Multiplier { get; }
        public long BonusUnits { get; }

        public PrizeDefinition(PrizeKind kind, decimal multiplier, long bonusUnits)
        {
            Kind = kind;
            Multiplier = multiplier;
            BonusUnits = bonusUnits;
        }

        public static PrizeDefinition ForMultiplier(decimal multiplier) => new PrizeDefinition(PrizeKind.Multiplier, multiplier, 0);
        public static PrizeDefinition ForBonus(long units) => new PrizeDefinition(PrizeKind.Bonus, 0m, units);

        /// <summary>
        /// The award in whole cents. A multiplier is the full credited payout (no stake returned on top);
        /// a bonus is a fixed amount whatever the bet. Returns false if the award is not a whole number of cents,
        /// because the engine never rounds money.
        /// </summary>
        public bool TryGetAwardCents(long betCents, long bonusUnitCents, out long awardCents)
        {
            if (Kind == PrizeKind.Bonus)
            {
                awardCents = checked(BonusUnits * bonusUnitCents);
                return true;
            }

            var exact = Multiplier * betCents;
            if (exact != decimal.Truncate(exact))
            {
                awardCents = 0;
                return false;
            }

            awardCents = (long)exact;
            return true;
        }

        public string Describe()
        {
            return Kind == PrizeKind.Bonus ? "bonus " + BonusUnits : Multiplier.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x";
        }

        // decimal equality ignores trailing zeros (2 == 2.0), so a bag survives a JSON round trip unchanged.
        public bool Equals(PrizeDefinition other) => Kind == other.Kind && Multiplier == other.Multiplier && BonusUnits == other.BonusUnits;
        public override bool Equals(object obj) => obj is PrizeDefinition other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ Multiplier.GetHashCode() ^ BonusUnits.GetHashCode();
    }

    /// <summary>A Regular or Big bag, expanded into one entry per ticket.</summary>
    public sealed class Bag
    {
        public string Name { get; }
        public IReadOnlyList<PrizeDefinition> Tickets { get; }
        public int TicketCount => Tickets.Count;
        public decimal MultiplierTotal { get; }
        public long BonusUnitsTotal { get; }

        public Bag(string name, IEnumerable<PrizeDefinition> tickets)
        {
            Name = name;
            Tickets = tickets.ToList();
            MultiplierTotal = Tickets.Where(t => t.Kind == PrizeKind.Multiplier).Sum(t => t.Multiplier);
            BonusUnitsTotal = Tickets.Where(t => t.Kind == PrizeKind.Bonus).Sum(t => t.BonusUnits);
        }

        public static Bag FromEntries(string name, IEnumerable<BagEntryData> entries)
        {
            var tickets = new List<PrizeDefinition>();
            foreach (var entry in entries)
            {
                var prize = entry.Multiplier.HasValue
                    ? PrizeDefinition.ForMultiplier(entry.Multiplier.Value)
                    : PrizeDefinition.ForBonus(entry.BonusUnits ?? 0);
                for (var i = 0; i < entry.Count; i++) tickets.Add(prize);
            }
            return new Bag(name, tickets);
        }

        /// <summary>The bag written back as compact entries (used to snapshot a deal's bags).</summary>
        public List<BagEntryData> ToEntries()
        {
            return Tickets
                .GroupBy(t => t)
                .Select(g => g.Key.Kind == PrizeKind.Bonus
                    ? new BagEntryData { BonusUnits = g.Key.BonusUnits, Count = g.Count() }
                    : new BagEntryData { Multiplier = g.Key.Multiplier, Count = g.Count() })
                .ToList();
        }
    }

    public sealed class ModeDefinition
    {
        public string Name { get; }
        public Bag Regular { get; }
        public Bag Big { get; }

        public ModeDefinition(string name, Bag regular, Bag big)
        {
            Name = name;
            Regular = regular;
            Big = big;
        }

        /// <summary>Tickets in one paired cycle (one Regular cycle plus one Big cycle).</summary>
        public int TicketsPerPairedCycle => Regular.TicketCount + Big.TicketCount;
    }
}
