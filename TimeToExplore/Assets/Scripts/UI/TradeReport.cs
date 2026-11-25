using System;
using System.Collections.Generic;
using System.Globalization;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.UI
{
    /// <summary>One offer as the trade panel shows it: the commodity and the
    /// station's two price labels.</summary>
    public readonly struct TradeOfferRow
    {
        public readonly CommodityData Commodity;
        public readonly int BuyPrice;
        public readonly int SellPrice;

        public TradeOfferRow(CommodityData commodity, int buyPrice, int sellPrice)
        {
            Commodity = commodity;
            BuyPrice = buyPrice;
            SellPrice = sellPrice;
        }
    }

    /// <summary>
    /// The text of the trade panel: the offers in market order, the hold's cargo
    /// line, and the per-unit volume and mass. Pure data with no Unity API, so
    /// the panel's content is tested without a scene, as with
    /// <see cref="BodyDetails"/> and <see cref="FlightReport"/>
    /// ([ADR-020](/decisions/adr-020-trade-catalogue.md)).
    /// </summary>
    public static class TradeReport
    {
        /// <summary>
        /// The body's offers in the catalogue's own market order, or an empty
        /// list when the entity has no market. An offer whose commodity does not
        /// resolve is skipped: <see cref="TradeCatalogue.Parse"/> rejects that,
        /// but the panel must not throw if a hand-built catalogue slips through.
        /// </summary>
        public static List<TradeOfferRow> Rows(TradeCatalogue catalogue, string bodyId)
        {
            var rows = new List<TradeOfferRow>();
            if (catalogue == null || !catalogue.TryGetMarket(bodyId, out MarketData market))
                return rows;

            foreach (TradeOfferData offer in market.offers ?? Array.Empty<TradeOfferData>())
            {
                if (offer == null) continue;
                if (!catalogue.TryGetCommodity(offer.commodityId, out CommodityData commodity))
                    continue;

                rows.Add(new TradeOfferRow(commodity, offer.buyPrice, offer.sellPrice));
            }

            return rows;
        }

        /// <summary>
        /// The hold in one line: used and free volume and the derived mass. The
        /// volume keeps one decimal so a part-loaded line reads as a figure
        /// rather than rounding to a whole crate; the mass is in kilograms with
        /// a thousands separator, the same human unit the details screen uses for
        /// a body (see <see cref="BodyDetails.FormatMass"/>).
        /// </summary>
        public static string CargoText(CargoHold hold, TradeCatalogue catalogue)
        {
            double used = hold.UsedM3;
            double free = hold.FreeM3;
            double mass = hold.MassKg(catalogue);

            return $"Cargo {Volume(used)} / {Volume(hold.CapacityM3)} m³"
                   + $" · free {Volume(free)} m³ · {Kilograms(mass)}";
        }

        /// <summary>Volume one unit takes, for the panel's volume column.</summary>
        public static string VolumeText(double m3PerUnit) => Number(m3PerUnit) + " m³/u";

        /// <summary>Mass one unit adds, for the panel's mass column.</summary>
        public static string MassText(double kgPerUnit) => Number(kgPerUnit) + " kg/u";

        /// <summary>One decimal, invariant, so a volume never reads "1,0" in a
        /// comma-decimal locale.</summary>
        private static string Volume(double cubicMetres) =>
            cubicMetres.ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>Whole kilograms with a thousands separator. Commodity mass
        /// per unit is a whole number of kilograms, and this keeps a partly
        /// loaded hold readable instead of the scientific notation the body
        /// panel uses for planetary masses.</summary>
        private static string Kilograms(double kilograms) =>
            kilograms.ToString("#,##0.##", CultureInfo.InvariantCulture) + " kg";

        /// <summary>Invariant, grouped, and without trailing zeros: 1 rather
        /// than 1.0, and 1,000 rather than 1000.</summary>
        private static string Number(double value) =>
            value.ToString("#,##0.###", CultureInfo.InvariantCulture);
    }
}
