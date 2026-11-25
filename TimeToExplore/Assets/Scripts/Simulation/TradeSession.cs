using System;
using System.Collections.Generic;
using TimeToExplore.Core;

namespace TimeToExplore.Simulation
{
    /// <summary>
    /// The market in front of the ship: which offers a body has, whether the
    /// ship may trade there, and the unit moves between the station and the
    /// hold. Prices are labels in the first pass — a transaction moves units
    /// only, with no balance and no charge ([ADR-020](/decisions/adr-020-trade-catalogue.md)).
    ///
    /// Plain C# with no Unity reference.
    /// </summary>
    public sealed class TradeSession
    {
        private static readonly TradeOfferData[] NoOffers = Array.Empty<TradeOfferData>();

        private readonly TradeCatalogue _catalogue;
        private readonly CargoHold _hold;

        public TradeSession(TradeCatalogue catalogue, CargoHold hold)
        {
            _catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            _hold = hold ?? throw new ArgumentNullException(nameof(hold));
        }

        public TradeCatalogue Catalogue => _catalogue;

        public CargoHold Hold => _hold;

        public bool HasMarket(string bodyId) => _catalogue.HasMarket(bodyId);

        /// <summary>The body's offers, or an empty list when it has no market.</summary>
        public IReadOnlyList<TradeOfferData> OffersFor(string bodyId)
        {
            if (!_catalogue.TryGetMarket(bodyId, out MarketData market) || market.offers == null)
                return NoOffers;
            return market.offers;
        }

        /// <summary>True only when the ship has arrived and its arrival target
        /// is the body. There is no second proximity or speed test: the flight
        /// model's terminal state is the answer.</summary>
        public static bool IsOpen(FlightStatus status, string targetId, string bodyId) =>
            status == FlightStatus.Arrived
            && !string.IsNullOrEmpty(bodyId)
            && string.Equals(targetId, bodyId, StringComparison.Ordinal);

        /// <summary>The pure gate plus the requirement that the body has a
        /// market.</summary>
        public bool IsOpenAt(FlightSession flight, BodyRuntime body)
        {
            if (flight == null || body == null) return false;
            return IsOpen(flight.Status, flight.TargetId, body.Id) && HasMarket(body.Id);
        }

        /// <summary>Buy up to <paramref name="units"/> at a body, clamped to
        /// the hold's free volume. Returns false when the market, the offer or
        /// the request is missing, or when nothing fit.</summary>
        public bool TryBuy(string bodyId, string commodityId, int units, out int moved)
        {
            moved = 0;
            if (units <= 0) return false;
            if (!TryFindOffer(bodyId, commodityId, out _)) return false;
            if (!_catalogue.TryGetCommodity(commodityId, out CommodityData commodity)) return false;

            moved = _hold.Add(commodity, units);
            return moved > 0;
        }

        /// <summary>Sell up to <paramref name="units"/> of a commodity at a
        /// body, clamped to what the hold carries. Returns false when the
        /// market, the offer or the request is missing, or when nothing moved.</summary>
        public bool TrySell(string bodyId, string commodityId, int units, out int moved)
        {
            moved = 0;
            if (units <= 0) return false;
            if (!TryFindOffer(bodyId, commodityId, out _)) return false;

            moved = _hold.Remove(commodityId, units);
            return moved > 0;
        }

        /// <summary>Units of a commodity the empty part of the hold can still
        /// take at a body, for the panel's <c>+max</c>. Zero when the offer is
        /// missing.</summary>
        public int MaxBuyable(string bodyId, string commodityId)
        {
            if (!TryFindOffer(bodyId, commodityId, out _)) return 0;
            if (!_catalogue.TryGetCommodity(commodityId, out CommodityData commodity)) return 0;
            return _hold.MaxUnits(commodity);
        }

        private bool TryFindOffer(string bodyId, string commodityId, out TradeOfferData offer)
        {
            offer = null;
            if (string.IsNullOrEmpty(commodityId)) return false;
            if (!_catalogue.TryGetMarket(bodyId, out MarketData market)) return false;

            foreach (TradeOfferData candidate in market.offers ?? NoOffers)
            {
                if (candidate != null
                    && string.Equals(candidate.commodityId, commodityId, StringComparison.Ordinal))
                {
                    offer = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
