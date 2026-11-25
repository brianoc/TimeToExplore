using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TimeToExplore.Core;
using UnityEngine;

namespace TimeToExplore.Tests.EditMode
{
    [Category("core")]
    public class TradeCatalogueTests
    {
        private static string _json;
        private static TradeCatalogue _catalogue;

        private static string ShippedJson
        {
            get
            {
                if (_json == null)
                {
                    string path = Path.Combine(
                        Application.dataPath, "Resources", "trade_catalogue.json");
                    _json = File.ReadAllText(path);
                }

                return _json;
            }
        }

        private static TradeCatalogue Catalogue
        {
            get
            {
                if (_catalogue == null)
                    _catalogue = TradeCatalogueLoader.Load(ShippedJson);
                return _catalogue;
            }
        }

        [Test]
        public void ShippedFileLoadsWithTheDocumentedShape()
        {
            Assert.That(Catalogue.Commodities.Count, Is.EqualTo(4));
            Assert.That(Catalogue.Markets.Count, Is.EqualTo(8));
            Assert.That(Catalogue.Markets.All(market => market.offers != null), Is.True,
                "every market needs an offers array, even an empty one");
        }

        [Test]
        public void ShippedCommoditiesAreTheDocumentedFour()
        {
            string[] ids = Catalogue.Commodities.Select(commodity => commodity.id).ToArray();
            CollectionAssert.AreEquivalent(
                new[] { "water", "food", "machinery", "rare_metals" }, ids);
        }

        [Test]
        public void EveryMarketResolvesToACatalogueBody()
        {
            var bodies = new Dictionary<string, BodyData>(StringComparer.Ordinal);
            foreach (BodyData body in CatalogueFixture.Data.bodies)
                bodies[body.id] = body;

            foreach (MarketData market in Catalogue.Markets)
            {
                Assert.That(bodies.ContainsKey(market.stationId), Is.True,
                    $"market '{market.stationId}' has no body in solar_system.json");
            }
        }

        [Test]
        public void EarthAndMarsArePlanetMarkets()
        {
            Assert.That(CatalogueFixture.Find("earth").kind, Is.EqualTo("planet"));
            Assert.That(CatalogueFixture.Find("mars").kind, Is.EqualTo("planet"));
            Assert.That(Catalogue.HasMarket("earth"), Is.True);
            Assert.That(Catalogue.HasMarket("mars"), Is.True);
        }

        [Test]
        public void GatewayHasFourOffersAndHeliosWatchHasThree()
        {
            Assert.That(Catalogue.TryGetMarket("gateway", out MarketData gateway), Is.True);
            Assert.That(gateway.offers.Length, Is.EqualTo(4));

            Assert.That(Catalogue.TryGetMarket("helios_watch", out MarketData heliosWatch), Is.True);
            Assert.That(heliosWatch.offers.Length, Is.EqualTo(3));
        }

        [Test]
        public void EveryOfferResolvesToACommodity()
        {
            foreach (MarketData market in Catalogue.Markets)
            {
                foreach (TradeOfferData offer in market.offers)
                {
                    Assert.That(Catalogue.TryGetCommodity(offer.commodityId, out CommodityData commodity),
                        Is.True, $"{market.stationId} offers unknown '{offer.commodityId}'");
                    Assert.That(commodity.volumePerUnitM3, Is.GreaterThan(0.0), commodity.id);
                    Assert.That(commodity.massPerUnitKg, Is.GreaterThan(0.0), commodity.id);
                    Assert.That(offer.sellPrice, Is.LessThanOrEqualTo(offer.buyPrice),
                        $"{market.stationId} pays more for '{offer.commodityId}' than it charges");
                }
            }
        }

        [Test]
        public void MissesReturnFalseAndCommodityThrows()
        {
            Assert.That(Catalogue.TryGetCommodity("unobtainium", out CommodityData commodity), Is.False);
            Assert.That(commodity, Is.Null);

            Assert.That(Catalogue.TryGetMarket("pluto", out MarketData market), Is.False);
            Assert.That(market, Is.Null);
            Assert.That(Catalogue.HasMarket("pluto"), Is.False);

            Assert.Throws<KeyNotFoundException>(() => Catalogue.Commodity("unobtainium"));
        }

        [Test]
        public void DuplicateCommodityIsRejected()
        {
            AssertRejected(
                FileJson(
                    CommodityJson("water") + ", " + CommodityJson("water"),
                    MarketJson("gateway", OfferJson("water"))),
                "duplicate commodity id 'water'");
        }

        [Test]
        public void DuplicateMarketIsRejected()
        {
            AssertRejected(
                FileJson(
                    CommodityJson("water"),
                    MarketJson("gateway", OfferJson("water")) + ", "
                    + MarketJson("gateway", OfferJson("water"))),
                "duplicate market stationId 'gateway'");
        }

        [Test]
        public void UnknownCommodityInAMarketIsRejected()
        {
            AssertRejected(
                FileJson(
                    CommodityJson("water"),
                    MarketJson("gateway", OfferJson("unobtainium"))),
                "unknown commodity 'unobtainium'");
        }

        [Test]
        public void DuplicateOfferInOneMarketIsRejected()
        {
            AssertRejected(
                FileJson(
                    CommodityJson("water"),
                    MarketJson("gateway", OfferJson("water"), OfferJson("water"))),
                "lists commodity 'water' twice");
        }

        [Test]
        public void ZeroVolumeIsRejected()
        {
            AssertRejected(
                FileJson(
                    CommodityJson("water", volume: "0.0"),
                    MarketJson("gateway", OfferJson("water"))),
                "must be positive");
        }

        [Test]
        public void ZeroMassIsRejected()
        {
            AssertRejected(
                FileJson(
                    CommodityJson("water", mass: "0.0"),
                    MarketJson("gateway", OfferJson("water"))),
                "must be positive");
        }

        [Test]
        public void SellPriceAboveBuyPriceIsRejected()
        {
            AssertRejected(
                FileJson(
                    CommodityJson("water"),
                    MarketJson("gateway", OfferJson("water", buy: 6, sell: 8))),
                "above buyPrice");
        }

        [Test]
        public void NegativePriceIsRejected()
        {
            AssertRejected(
                FileJson(
                    CommodityJson("water"),
                    MarketJson("gateway", OfferJson("water", buy: -1))),
                "cannot be negative");

            AssertRejected(
                FileJson(
                    CommodityJson("water"),
                    MarketJson("gateway", OfferJson("water", sell: -1))),
                "cannot be negative");
        }

        private static void AssertRejected(string json, string expectedMessage)
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => TradeCatalogue.Parse(json));
            StringAssert.Contains(expectedMessage, error.Message);
        }

        private static string FileJson(string commodities, string markets) =>
            $"{{ \"commodities\": [{commodities}], \"markets\": [{markets}] }}";

        private static string CommodityJson(string id, string volume = "1.0", string mass = "1000.0") =>
            $"{{ \"id\": \"{id}\", \"name\": \"{id}\", \"volumePerUnitM3\": {volume}, "
            + $"\"massPerUnitKg\": {mass}, \"description\": \"Test row.\" }}";

        private static string MarketJson(string stationId, params string[] offers) =>
            $"{{ \"stationId\": \"{stationId}\", \"offers\": [{string.Join(", ", offers)}] }}";

        private static string OfferJson(string commodityId, int buy = 8, int sell = 6) =>
            $"{{ \"commodityId\": \"{commodityId}\", \"buyPrice\": {buy}, \"sellPrice\": {sell} }}";
    }
}
