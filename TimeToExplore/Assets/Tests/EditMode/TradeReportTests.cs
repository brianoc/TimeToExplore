using System.Collections.Generic;
using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;
using TimeToExplore.UI;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The trade panel's text, without a scene: the offer rows in market order,
    /// the per-unit volume and mass, and the hold's cargo line
    /// ([ADR-020](/decisions/adr-020-trade-catalogue.md)).
    /// </summary>
    [Category("presentation")]
    public class TradeReportTests
    {
        private static TradeCatalogue Catalogue => TradeFixture.Catalogue;

        [Test]
        public void GatewayListsItsFourOffersInCatalogueOrder()
        {
            List<TradeOfferRow> rows = TradeReport.Rows(Catalogue, "gateway");

            Assert.That(rows.Count, Is.EqualTo(4));
            AssertOffer(rows[0], "water", "Water", 8, 6);
            AssertOffer(rows[1], "food", "Food", 12, 9);
            AssertOffer(rows[2], "machinery", "Machinery", 20, 15);
            AssertOffer(rows[3], "rare_metals", "Rare metals", 60, 48);
        }

        [Test]
        public void HeliosWatchListsItsThreeOffers()
        {
            List<TradeOfferRow> rows = TradeReport.Rows(Catalogue, "helios_watch");

            Assert.That(rows.Count, Is.EqualTo(3));
            AssertOffer(rows[0], "water", "Water", 10, 7);
            AssertOffer(rows[1], "food", "Food", 14, 10);
            AssertOffer(rows[2], "machinery", "Machinery", 24, 18);
        }

        [Test]
        public void ABodyWithoutAMarketHasNoRows()
        {
            Assert.That(TradeReport.Rows(Catalogue, "moon"), Is.Empty);
            Assert.That(TradeReport.Rows(Catalogue, "sun"), Is.Empty,
                "the star is not a market");
            Assert.That(TradeReport.Rows(Catalogue, "not_a_body"), Is.Empty);
            Assert.That(TradeReport.Rows(Catalogue, null), Is.Empty);
            Assert.That(TradeReport.Rows(null, "gateway"), Is.Empty,
                "a missing catalogue offers nothing instead of throwing");
        }

        [Test]
        public void UnitTextsUseHumanUnitsWithoutTrailingZeros()
        {
            Assert.That(TradeReport.VolumeText(1.0), Is.EqualTo("1 m³/u"));
            Assert.That(TradeReport.VolumeText(1.2), Is.EqualTo("1.2 m³/u"));
            Assert.That(TradeReport.VolumeText(1.5), Is.EqualTo("1.5 m³/u"));
            Assert.That(TradeReport.VolumeText(0.4), Is.EqualTo("0.4 m³/u"));

            Assert.That(TradeReport.MassText(1_000.0), Is.EqualTo("1,000 kg/u"));
            Assert.That(TradeReport.MassText(400.0), Is.EqualTo("400 kg/u"));
            Assert.That(TradeReport.MassText(2_200.0), Is.EqualTo("2,200 kg/u"));
        }

        [Test]
        public void TheCargoLineReadsUsedFreeAndMassForAnEmptyHold()
        {
            var hold = new CargoHold(CargoHold.DefaultCapacityM3);

            Assert.That(TradeReport.CargoText(hold, Catalogue),
                Is.EqualTo("Cargo 0.0 / 40.0 m³ · free 40.0 m³ · 0 kg"));
        }

        [Test]
        public void TheCargoLineFollowsAPartlyFilledHold()
        {
            var hold = new CargoHold(CargoHold.DefaultCapacityM3);
            hold.Add(Catalogue.Commodity("water"), 5);

            Assert.That(TradeReport.CargoText(hold, Catalogue),
                Is.EqualTo("Cargo 5.0 / 40.0 m³ · free 35.0 m³ · 5,000 kg"));

            hold.Add(Catalogue.Commodity("rare_metals"), 2);

            Assert.That(TradeReport.CargoText(hold, Catalogue),
                Is.EqualTo("Cargo 5.8 / 40.0 m³ · free 34.2 m³ · 9,400 kg"),
                "the mass is the derived figure, never a stored one");
        }

        private static void AssertOffer(TradeOfferRow row, string id, string name,
            int buyPrice, int sellPrice)
        {
            Assert.That(row.Commodity, Is.Not.Null);
            Assert.That(row.Commodity.id, Is.EqualTo(id));
            Assert.That(row.Commodity.name, Is.EqualTo(name));
            Assert.That(row.BuyPrice, Is.EqualTo(buyPrice), id + " buy price");
            Assert.That(row.SellPrice, Is.EqualTo(sellPrice), id + " sell price");
            Assert.That(row.Commodity.volumePerUnitM3, Is.GreaterThan(0.0));
            Assert.That(row.Commodity.massPerUnitKg, Is.GreaterThan(0.0));
        }
    }
}
