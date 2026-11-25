using NUnit.Framework;
using TimeToExplore.Core;
using TimeToExplore.Simulation;

namespace TimeToExplore.Tests.EditMode
{
    /// <summary>
    /// Cargo arithmetic: volume is the only limit, units are whole, and mass is
    /// derived from the catalogue rather than stored
    /// ([ADR-020](/decisions/adr-020-trade-catalogue.md)).
    /// </summary>
    [Category("flight")]
    public class CargoHoldTests
    {
        private static TradeCatalogue Catalogue => TradeFixture.Catalogue;

        private static CommodityData Commodity(string id) => Catalogue.Commodity(id);

        private static CargoHold NewHold() => new CargoHold(CargoHold.DefaultCapacityM3);

        [Test]
        public void WaterFillsTheHold()
        {
            CargoHold hold = NewHold();

            int moved = hold.Add(Commodity("water"), 40);

            Assert.That(moved, Is.EqualTo(40));
            Assert.That(hold.UnitsOf("water"), Is.EqualTo(40));
            Assert.That(hold.VolumeOf("water"), Is.EqualTo(40.0).Within(1e-9));
            Assert.That(hold.UsedM3, Is.EqualTo(40.0).Within(1e-9));
            Assert.That(hold.FreeM3, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void FoodTakesThirtyThreeUnits()
        {
            CargoHold hold = NewHold();

            int moved = hold.Add(Commodity("food"), 40);

            Assert.That(moved, Is.EqualTo(33));
            Assert.That(hold.UsedM3, Is.EqualTo(39.6).Within(1e-9));
            Assert.That(hold.FreeM3, Is.EqualTo(0.4).Within(1e-9));
        }

        [Test]
        public void MachineryTakesTwentySixUnits()
        {
            CargoHold hold = NewHold();

            int moved = hold.Add(Commodity("machinery"), 40);

            Assert.That(moved, Is.EqualTo(26));
            Assert.That(hold.UsedM3, Is.EqualTo(39.0).Within(1e-9));
            Assert.That(hold.FreeM3, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void RareMetalsTakeOneHundredUnits()
        {
            CargoHold hold = NewHold();

            int moved = hold.Add(Commodity("rare_metals"), 100);

            Assert.That(moved, Is.EqualTo(100),
                "the epsilon must absorb 40 / 0.4 rounding to 99");
            Assert.That(hold.UsedM3, Is.EqualTo(40.0).Within(1e-9));
            Assert.That(hold.FreeM3, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void AMixedLoadReportsEachLineAndTheFreeVolume()
        {
            CargoHold hold = NewHold();

            hold.Add(Commodity("water"), 10);
            hold.Add(Commodity("food"), 5);
            hold.Add(Commodity("machinery"), 4);
            hold.Add(Commodity("rare_metals"), 10);

            Assert.That(hold.UnitsOf("water"), Is.EqualTo(10));
            Assert.That(hold.UnitsOf("food"), Is.EqualTo(5));
            Assert.That(hold.UnitsOf("machinery"), Is.EqualTo(4));
            Assert.That(hold.UnitsOf("rare_metals"), Is.EqualTo(10));
            Assert.That(hold.UsedM3, Is.EqualTo(26.0).Within(1e-9));
            Assert.That(hold.FreeM3, Is.EqualTo(14.0).Within(1e-9));
        }

        [Test]
        public void AnOverCapacityAddIsClamped()
        {
            CargoHold hold = NewHold();

            Assert.That(hold.Add(Commodity("water"), 41), Is.EqualTo(40));
            Assert.That(hold.UnitsOf("water"), Is.EqualTo(40));
            Assert.That(hold.FreeM3, Is.EqualTo(0.0).Within(1e-9));

            CargoHold other = NewHold();
            Assert.That(other.Add(Commodity("food"), 1_000), Is.EqualTo(33));
            Assert.That(other.Add(Commodity("water"), 1), Is.Zero,
                "a full hold takes nothing more");
        }

        [Test]
        public void MaxUnitsRoundsDownToWholeUnits()
        {
            CargoHold hold = NewHold();

            Assert.That(hold.MaxUnits(Commodity("water")), Is.EqualTo(40));
            Assert.That(hold.MaxUnits(Commodity("food")), Is.EqualTo(33));
            Assert.That(hold.MaxUnits(Commodity("machinery")), Is.EqualTo(26));
            Assert.That(hold.MaxUnits(Commodity("rare_metals")), Is.EqualTo(100));
        }

        [Test]
        public void CanAddAgreesWithWhatAddActuallyTakes()
        {
            foreach (CommodityData commodity in Catalogue.Commodities)
            {
                foreach (int units in new[] { 1, 10, 33, 34, 100, 1_000 })
                {
                    CargoHold hold = NewHold();
                    bool canAdd = hold.CanAdd(commodity, units);
                    int moved = hold.Add(commodity, units);

                    Assert.That(canAdd, Is.EqualTo(moved == units),
                        $"{commodity.id} × {units}: CanAdd said {canAdd}, Add took {moved}");
                }
            }
        }

        [Test]
        public void RemoveClampsToWhatIsHeldAndReportsTheMovedCount()
        {
            CargoHold hold = NewHold();
            hold.Add(Commodity("water"), 10);

            Assert.That(hold.Remove("water", 4), Is.EqualTo(4));
            Assert.That(hold.UnitsOf("water"), Is.EqualTo(6));

            Assert.That(hold.Remove("water", 100), Is.EqualTo(6),
                "a sale larger than the line moves only the line");
            Assert.That(hold.UnitsOf("water"), Is.Zero);
            Assert.That(hold.UsedM3, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(hold.FreeM3, Is.EqualTo(40.0).Within(1e-9));
        }

        [Test]
        public void RemoveWithNoUnitsMovesNothing()
        {
            CargoHold hold = NewHold();
            hold.Add(Commodity("food"), 3);

            Assert.That(hold.Remove("food", 0), Is.Zero);
            Assert.That(hold.Remove("food", -2), Is.Zero);
            Assert.That(hold.Remove("water", 1), Is.Zero, "nothing of that commodity is held");
            Assert.That(hold.Remove("unobtainium", 1), Is.Zero);
            Assert.That(hold.UnitsOf("food"), Is.EqualTo(3));
        }

        [Test]
        public void MassIsDerivedFromTheLoad()
        {
            CargoHold hold = NewHold();
            hold.Add(Commodity("water"), 2);
            hold.Add(Commodity("food"), 3);
            hold.Add(Commodity("machinery"), 1);
            hold.Add(Commodity("rare_metals"), 2);

            // 2 × 1,000 + 3 × 400 + 1 × 1,200 + 2 × 2,200
            Assert.That(hold.MassKg(Catalogue), Is.EqualTo(8_800.0).Within(1e-9));

            hold.Remove("water", 1);
            Assert.That(hold.MassKg(Catalogue), Is.EqualTo(7_800.0).Within(1e-9),
                "mass follows the new count; nothing was stored to go stale");

            hold.Add(Commodity("machinery"), 1);
            Assert.That(hold.MassKg(Catalogue), Is.EqualTo(9_000.0).Within(1e-9));
        }

        [Test]
        public void UnknownIdsReadAsEmpty()
        {
            CargoHold hold = NewHold();

            Assert.That(hold.UnitsOf("unobtainium"), Is.Zero);
            Assert.That(hold.VolumeOf("unobtainium"), Is.Zero);
            Assert.That(hold.UnitsOf(null), Is.Zero);
            Assert.That(hold.VolumeOf(null), Is.Zero);
        }

        [Test]
        public void CapacityMustBePositiveAndFinite()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new CargoHold(0.0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new CargoHold(-1.0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new CargoHold(double.NaN));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new CargoHold(double.PositiveInfinity));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new CargoHold(double.NegativeInfinity));
        }
    }
}
