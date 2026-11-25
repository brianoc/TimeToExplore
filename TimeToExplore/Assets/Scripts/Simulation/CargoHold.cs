using System;
using System.Collections.Generic;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// The ship's cargo: a capacity in cubic metres and a whole-unit count per
    /// commodity. Volume is the only limit. Mass per unit is declared in the
    /// catalogue and derived on demand, never stored, so a hold cannot disagree
    /// with its load.
    ///
    /// Plain C# with no Unity reference ([ADR-020](/decisions/adr-020-trade-catalogue.md)),
    /// so the cargo arithmetic is testable without a scene.
    /// </summary>
    public sealed class CargoHold
    {
        /// <summary>The single ship's hold: 40 m³.</summary>
        public const double DefaultCapacityM3 = 40.0;

        /// <summary>Absorbs the representation error of a volume divided by a
        /// unit size, so a 40 m³ hold really takes 100 units of 0.4 m³ rather
        /// than 99. It is far smaller than any meaningful cargo volume.</summary>
        private const double VolumeEpsilonM3 = 1e-9;

        private readonly double _capacityM3;

        /// <summary>Unit count per commodity id. A line is removed when it
        /// reaches zero, so the view never carries empty rows.</summary>
        private readonly Dictionary<string, int> _units =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Volume one unit of a commodity takes, captured when the
        /// line was created. The catalogue is read-only but <see cref="UsedM3"/>
        /// has no catalogue parameter, so the hold keeps the one number it needs
        /// to add volumes up.</summary>
        private readonly Dictionary<string, double> _volumePerUnitM3 =
            new Dictionary<string, double>(StringComparer.Ordinal);

        public CargoHold(double capacityM3)
        {
            if (!double.IsFinite(capacityM3) || !(capacityM3 > 0.0))
                throw new ArgumentOutOfRangeException(nameof(capacityM3), capacityM3,
                    "Cargo capacity must be a positive, finite number of cubic metres.");
            _capacityM3 = capacityM3;
        }

        public double CapacityM3 => _capacityM3;

        /// <summary>Sum of units × volume per unit over every line.</summary>
        public double UsedM3
        {
            get
            {
                double used = 0.0;
                foreach (KeyValuePair<string, int> line in _units)
                    used += line.Value * _volumePerUnitM3[line.Key];
                return used;
            }
        }

        public double FreeM3 => _capacityM3 - UsedM3;

        /// <summary>Read-only view of the unit counts, for the HUD.</summary>
        public IReadOnlyDictionary<string, int> Units => _units;

        public int UnitsOf(string commodityId)
        {
            if (string.IsNullOrEmpty(commodityId)) return 0;
            return _units.TryGetValue(commodityId, out int units) ? units : 0;
        }

        public double VolumeOf(string commodityId)
        {
            if (string.IsNullOrEmpty(commodityId)) return 0.0;
            if (!_units.TryGetValue(commodityId, out int units)) return 0.0;
            return units * _volumePerUnitM3[commodityId];
        }

        /// <summary>Whole units of a commodity that still fit in the free
        /// volume. The epsilon keeps an exact fit exact.</summary>
        public int MaxUnits(CommodityData commodity)
        {
            if (commodity == null) throw new ArgumentNullException(nameof(commodity));
            if (!(commodity.volumePerUnitM3 > 0.0)) return 0;

            double free = FreeM3 + VolumeEpsilonM3;
            if (!(free > 0.0)) return 0;

            double units = Math.Floor(free / commodity.volumePerUnitM3);
            return units >= int.MaxValue ? int.MaxValue : (int)units;
        }

        public bool CanAdd(CommodityData commodity, int units) =>
            units > 0 && units <= MaxUnits(commodity);

        /// <summary>Load up to <paramref name="units"/>, clamped to the free
        /// volume. Returns the units actually added.</summary>
        public int Add(CommodityData commodity, int units)
        {
            if (commodity == null) throw new ArgumentNullException(nameof(commodity));
            if (units <= 0) return 0;

            int accepted = Math.Min(units, MaxUnits(commodity));
            if (accepted <= 0) return 0;

            _units.TryGetValue(commodity.id, out int held);
            _units[commodity.id] = held + accepted;
            _volumePerUnitM3[commodity.id] = commodity.volumePerUnitM3;
            return accepted;
        }

        /// <summary>Unload up to <paramref name="units"/>, clamped to what is
        /// held. Returns the units actually removed.</summary>
        public int Remove(string commodityId, int units)
        {
            if (units <= 0 || string.IsNullOrEmpty(commodityId)) return 0;
            if (!_units.TryGetValue(commodityId, out int held) || held <= 0) return 0;

            int removed = Math.Min(units, held);
            int left = held - removed;
            if (left == 0)
            {
                _units.Remove(commodityId);
                _volumePerUnitM3.Remove(commodityId);
            }
            else
            {
                _units[commodityId] = left;
            }

            return removed;
        }

        /// <summary>The load's mass, from units × mass per unit. An id the
        /// catalogue does not know contributes nothing; it cannot happen through
        /// <see cref="Add"/>, but the display must not throw if it does.</summary>
        public double MassKg(TradeCatalogue catalogue)
        {
            if (catalogue == null) throw new ArgumentNullException(nameof(catalogue));

            double mass = 0.0;
            foreach (KeyValuePair<string, int> line in _units)
            {
                if (catalogue.TryGetCommodity(line.Key, out CommodityData commodity))
                    mass += line.Value * commodity.massPerUnitKg;
            }

            return mass;
        }
    }
}
