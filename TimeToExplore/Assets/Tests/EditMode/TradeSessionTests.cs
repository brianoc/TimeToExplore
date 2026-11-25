using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// The trade gate and the unit transfers: a transaction needs an arrived
    /// target with a market, moves whole units, and charges nothing
    /// ([ADR-020](/decisions/adr-020-trade-catalogue.md)).
    /// </summary>
    [Category("flight")]
    public class TradeSessionTests
    {
        private static TradeCatalogue Catalogue => TradeFixture.Catalogue;

        private static CommodityData Commodity(string id) => Catalogue.Commodity(id);

        private static TradeSession NewSession(out CargoHold hold)
        {
            hold = new CargoHold(CargoHold.DefaultCapacityM3);
            return new TradeSession(Catalogue, hold);
        }

        [Test]
        public void TheGateIsOpenOnlyOnArrivalAtTheMatchingBody()
        {
            Assert.That(TradeSession.IsOpen(FlightStatus.Arrived, "gateway", "gateway"), Is.True);

            Assert.That(TradeSession.IsOpen(FlightStatus.Flying, "gateway", "gateway"), Is.False);
            Assert.That(TradeSession.IsOpen(FlightStatus.Idle, "gateway", "gateway"), Is.False);
            Assert.That(TradeSession.IsOpen(FlightStatus.Rejected, "gateway", "gateway"), Is.False);
            Assert.That(TradeSession.IsOpen(FlightStatus.Aborted, "gateway", "gateway"), Is.False);
            Assert.That(TradeSession.IsOpen(FlightStatus.Arrived, "gateway", "earth"), Is.False,
                "arrived elsewhere");
            Assert.That(TradeSession.IsOpen(FlightStatus.Arrived, null, "gateway"), Is.False);
            Assert.That(TradeSession.IsOpen(FlightStatus.Arrived, "gateway", null), Is.False);
        }

        [Test]
        public void OffersAreTheBodysRowsAndEmptyWithoutAMarket()
        {
            TradeSession trade = NewSession(out _);

            Assert.That(trade.OffersFor("gateway").Count, Is.EqualTo(4));
            Assert.That(trade.OffersFor("helios_watch").Count, Is.EqualTo(3));
            Assert.That(trade.OffersFor("luna").Count, Is.Zero,
                "a body without a market offers nothing");
            Assert.That(trade.OffersFor(null).Count, Is.Zero);
            Assert.That(trade.HasMarket("gateway"), Is.True);
            Assert.That(trade.HasMarket("luna"), Is.False);
        }

        [Test]
        public void BuyingAndSellingMoveUnitsAtGateway()
        {
            TradeSession trade = NewSession(out CargoHold hold);

            Assert.That(trade.TryBuy("gateway", "water", 10, out int bought), Is.True);
            Assert.That(bought, Is.EqualTo(10));
            Assert.That(hold.UnitsOf("water"), Is.EqualTo(10));

            Assert.That(trade.TrySell("gateway", "water", 4, out int sold), Is.True);
            Assert.That(sold, Is.EqualTo(4));
            Assert.That(hold.UnitsOf("water"), Is.EqualTo(6));

            Assert.That(trade.TrySell("gateway", "water", 100, out int all), Is.True);
            Assert.That(all, Is.EqualTo(6), "selling more than held moves only what is held");
            Assert.That(hold.UnitsOf("water"), Is.Zero);
        }

        [Test]
        public void APurchaseLargerThanTheHoldMovesOnlyWhatFits()
        {
            TradeSession trade = NewSession(out CargoHold hold);

            Assert.That(trade.TryBuy("gateway", "food", 100, out int bought), Is.True);
            Assert.That(bought, Is.EqualTo(33));
            Assert.That(hold.UnitsOf("food"), Is.EqualTo(33));
            Assert.That(hold.FreeM3, Is.EqualTo(0.4).Within(1e-9));
        }

        [Test]
        public void AnUnofferedCommodityMovesNothing()
        {
            TradeSession trade = NewSession(out CargoHold hold);

            Assert.That(Catalogue.HasMarket("helios_watch"), Is.True);
            Assert.That(trade.TryBuy("helios_watch", "rare_metals", 1, out int bought), Is.False);
            Assert.That(bought, Is.Zero);
            Assert.That(hold.UnitsOf("rare_metals"), Is.Zero);

            Assert.That(trade.TryBuy("gateway", "unobtainium", 1, out bought), Is.False);
            Assert.That(bought, Is.Zero);
        }

        [Test]
        public void AnEntityWithoutAMarketMovesNothing()
        {
            TradeSession trade = NewSession(out CargoHold hold);

            Assert.That(trade.TryBuy("luna", "water", 1, out int bought), Is.False);
            Assert.That(bought, Is.Zero);
            Assert.That(trade.TrySell("luna", "water", 1, out int sold), Is.False);
            Assert.That(sold, Is.Zero);
            Assert.That(hold.UnitsOf("water"), Is.Zero);
        }

        [Test]
        public void NonPositiveRequestsMoveNothing()
        {
            TradeSession trade = NewSession(out CargoHold hold);

            Assert.That(trade.TryBuy("gateway", "water", 0, out int bought), Is.False);
            Assert.That(bought, Is.Zero);
            Assert.That(trade.TryBuy("gateway", "water", -5, out bought), Is.False);
            Assert.That(bought, Is.Zero);
            Assert.That(trade.TrySell("gateway", "water", 0, out int sold), Is.False);
            Assert.That(sold, Is.Zero);
            Assert.That(hold.UnitsOf("water"), Is.Zero);
        }

        [Test]
        public void PricesAreUnchangedByATransaction()
        {
            TradeSession trade = NewSession(out _);
            TradeOfferData before = Offer(trade, "gateway", "water");

            trade.TryBuy("gateway", "water", 10, out _);
            trade.TrySell("gateway", "water", 4, out _);

            TradeOfferData after = Offer(trade, "gateway", "water");
            Assert.That(after.buyPrice, Is.EqualTo(before.buyPrice));
            Assert.That(after.sellPrice, Is.EqualTo(before.sellPrice));
            Assert.That(after.buyPrice, Is.EqualTo(8));
            Assert.That(after.sellPrice, Is.EqualTo(6));
        }

        [Test]
        public void MaxBuyableIsTheFreeVolumeInWholeUnits()
        {
            TradeSession trade = NewSession(out CargoHold hold);

            Assert.That(trade.MaxBuyable("gateway", "water"), Is.EqualTo(40));
            Assert.That(trade.MaxBuyable("gateway", "food"), Is.EqualTo(33));
            Assert.That(trade.MaxBuyable("gateway", "machinery"), Is.EqualTo(26));
            Assert.That(trade.MaxBuyable("gateway", "rare_metals"), Is.EqualTo(100));
            Assert.That(trade.MaxBuyable("gateway", "unobtainium"), Is.Zero);
            Assert.That(trade.MaxBuyable("helios_watch", "rare_metals"), Is.Zero,
                "the offer is missing");
            Assert.That(trade.MaxBuyable("luna", "water"), Is.Zero, "the market is missing");

            hold.Add(Commodity("water"), 5);
            Assert.That(trade.MaxBuyable("gateway", "water"), Is.EqualTo(35));
            Assert.That(trade.MaxBuyable("gateway", "food"), Is.EqualTo(29));
            Assert.That(trade.MaxBuyable("gateway", "machinery"), Is.EqualTo(23));
            Assert.That(trade.MaxBuyable("gateway", "rare_metals"), Is.EqualTo(87));
        }

        [Test]
        public void ArrivalInAPlanetOrbitOpensTheMarket()
        {
            SolarSystemModel model = CatalogueFixture.LoadModel();
            using (FlightSession flight = FlyTo(model, "gateway", "earth"))
            {
                TradeSession trade = new TradeSession(Catalogue,
                    new CargoHold(CargoHold.DefaultCapacityM3));

                Assert.That(flight.Status, Is.EqualTo(FlightStatus.Arrived));
                Assert.That(flight.TargetId, Is.EqualTo("earth"));
                Assert.That(trade.IsOpenAt(flight, model.Get("earth")), Is.True);
            }
        }

        [Test]
        public void ArrivalAtABodyWithoutAMarketLeavesTheGateClosed()
        {
            SolarSystemModel model = CatalogueFixture.LoadModel();
            using (FlightSession flight = FlyTo(model, "gateway", "moon"))
            {
                TradeSession trade = new TradeSession(Catalogue,
                    new CargoHold(CargoHold.DefaultCapacityM3));

                Assert.That(flight.Status, Is.EqualTo(FlightStatus.Arrived));
                Assert.That(flight.TargetId, Is.EqualTo("moon"));
                Assert.That(TradeSession.IsOpen(FlightStatus.Arrived, "moon", "moon"), Is.True);
                Assert.That(trade.HasMarket("moon"), Is.False);
                Assert.That(trade.IsOpenAt(flight, model.Get("moon")), Is.False,
                    "the pure gate is open but the body has no market");
            }
        }

        private static TradeOfferData Offer(TradeSession trade, string bodyId, string commodityId)
        {
            foreach (TradeOfferData offer in trade.OffersFor(bodyId))
            {
                if (offer.commodityId == commodityId) return offer;
            }

            Assert.Fail($"{bodyId} has no offer for {commodityId}");
            return null;
        }

        /// <summary>Fly a real session to a terminal arrival state, so the gate
        /// is proved against the same status the HUD reads. A station hold point
        /// is not flown here: the pure gate covers the status and target, and an
        /// arrival flight at Gateway costs too much for this suite; the station
        /// hold itself is already flown by <c>FlightSessionTests</c>.</summary>
        private static FlightSession FlyTo(SolarSystemModel model, string fromId, string toId)
        {
            GravityField gravity = FlightFixture.FullField(model);
            var planner = new TripPlanner(model, gravity);
            var flight = new FlightSession(model, gravity, planner);
            double epoch = model.CurrentJulianDate;

            flight.PlaceAt(fromId, epoch);
            TrajectoryPlan plan = flight.Begin(toId, epoch);
            Assert.That(plan.IsFeasible, Is.True, plan.InfeasibleReason);

            FlightStep step = flight.Advance(plan.ArrivalJulianDate, 200_000);
            Assert.That(step.BudgetExhausted, Is.False, "the budget must reach the arrival");
            Assert.That(step.Arrived, Is.True, $"flight to {toId} did not arrive");
            Assert.That(flight.Status, Is.EqualTo(FlightStatus.Arrived));

            return flight;
        }
    }
}
