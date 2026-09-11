using Dse.Core.Flow;
using Dse.Core.Graph;
using Dse.Core.Tests.Fakes;
using Dse.Core.Validation;
using Xunit;

namespace Dse.Core.Tests;

public class BulkBeltTests
{
    private const double Dt = 0.5;

    private static readonly MaterialType Ore = new("Ore", PayloadKind.Bulk);

    private static BulkBelt NewBelt(int cells = 5, double maxLinearDensity = 4.0, double maxSpeed = 1.0)
    {
        var belt = new BulkBelt("CV", length: cells * 0.5, cellSize: 0.5, maxSpeed: maxSpeed, maxLinearDensity: maxLinearDensity);
        belt.Initialize(TestContexts.Init(belt.Id, dt: Dt));
        return belt;
    }

    private static OutputPort<double> Drive(BulkBelt belt, double speed)
    {
        var setpoint = new OutputPort<double>("Out", "SP");
        setpoint.ConnectTo(belt.Speed);
        setpoint.Value = speed;
        return setpoint;
    }

    private static double[] Masses(BulkBelt belt)
    {
        var masses = new double[belt.CellCount];
        for (int i = 0; i < masses.Length; i++)
        {
            masses[i] = belt.Cells[i].Mass;
        }

        return masses;
    }

    private static void Deposit(BulkBelt belt, double mass) =>
        belt.Deposit(belt.In, BulkLot.Of(Ore, mass, default));

    [Fact]
    public void RejectsALengthThatIsNotAWholeNumberOfCells()
    {
        Assert.Throws<ArgumentException>(() => new BulkBelt("CV", 1.2, 0.5, 1.0, 4.0));
    }

    [Fact]
    public void RejectsNonPositiveConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkBelt("CV", 0.0, 0.5, 1.0, 4.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkBelt("CV", 1.0, 0.0, 1.0, 4.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkBelt("CV", 1.0, 0.5, 0.0, 4.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BulkBelt("CV", 1.0, 0.5, 1.0, 0.0));
    }

    [Fact]
    public void CflViolationIsReportedWithTheFix()
    {
        BulkBelt tooFast = NewBelt(maxSpeed: 2.0);

        ValidationError error = Assert.Single(tooFast.ValidateFlow(Dt));
        Assert.Equal("DSE006", error.Code);
        Assert.Equal(new[] { "CV" }, error.ComponentIds);
        Assert.Contains("cell", error.Message, StringComparison.Ordinal);

        Assert.Empty(NewBelt(maxSpeed: 1.0).ValidateFlow(Dt));
    }

    [Fact]
    public void DepositLandsInTheFirstCell()
    {
        BulkBelt belt = NewBelt();

        Deposit(belt, 1.0);

        Assert.Equal(new[] { 1.0, 0.0, 0.0, 0.0, 0.0 }, Masses(belt));
        Assert.Equal(1.0, belt.MassHeld, 9);
        Assert.Same(Ore, belt.Cells[0].Type);
    }

    [Fact]
    public void AtFullSpeedEveryCellShiftsOnePlacePerTick()
    {
        BulkBelt belt = NewBelt();
        Drive(belt, 1.0);
        Deposit(belt, 1.0);

        for (int i = 0; i < 4; i++)
        {
            belt.Advance(Dt);
        }

        Assert.Equal(new[] { 0.0, 0.0, 0.0, 0.0, 1.0 }, Masses(belt));
        Assert.Equal(1.0, belt.OfferMass(belt.Out), 9);
    }

    [Fact]
    public void AtHalfSpeedHalfOfEachCellMoves()
    {
        BulkBelt belt = NewBelt();
        Drive(belt, 0.5);
        Deposit(belt, 1.0);

        belt.Advance(Dt);
        Assert.Equal(new[] { 0.5, 0.5, 0.0, 0.0, 0.0 }, Masses(belt));

        belt.Advance(Dt);
        Assert.Equal(new[] { 0.25, 0.5, 0.25, 0.0, 0.0 }, Masses(belt));
    }

    [Fact]
    public void AStoppedBeltFreezesTheProfile()
    {
        BulkBelt belt = NewBelt();
        OutputPort<double> speed = Drive(belt, 1.0);
        Deposit(belt, 1.0);
        belt.Advance(Dt);
        belt.Advance(Dt);

        speed.Value = 0.0;
        for (int i = 0; i < 3; i++)
        {
            belt.Advance(Dt);
        }

        Assert.Equal(new[] { 0.0, 0.0, 1.0, 0.0, 0.0 }, Masses(belt));
        Assert.Equal(0.0, belt.OfferMass(belt.Out));
    }

    [Fact]
    public void OfferIsTheMovingFractionOfTheLastCellAndWithdrawTakesIt()
    {
        BulkBelt belt = NewBelt();
        Drive(belt, 1.0);
        Deposit(belt, 1.0);
        for (int i = 0; i < 4; i++)
        {
            belt.Advance(Dt);
        }

        BulkLot lot = belt.Withdraw(belt.Out, 0.4);

        Assert.Equal(0.4, lot.Mass, 9);
        Assert.Equal(0.6, belt.Cells[4].Mass, 9);
        Assert.Equal(0.6, belt.OfferMass(belt.Out), 9);
    }

    [Fact]
    public void AcceptIsTheRoomInTheFirstCell()
    {
        BulkBelt belt = NewBelt(maxLinearDensity: 4.0); // 4 kg/m x 0.5 m = 2 kg per cell

        Assert.Equal(2.0, belt.AcceptMass(belt.In), 9);
        Deposit(belt, 1.5);
        Assert.Equal(0.5, belt.AcceptMass(belt.In), 9);
    }

    [Fact]
    public void ABlockedDischargeBuildsLoadBackwardsWithoutExceedingCapacity()
    {
        BulkBelt belt = NewBelt(cells: 5, maxLinearDensity: 4.0);
        Drive(belt, 1.0);

        for (int tick = 0; tick < 20; tick++)
        {
            double room = belt.AcceptMass(belt.In);
            if (room > 0.0)
            {
                Deposit(belt, Math.Min(1.0, room));
            }

            belt.Advance(Dt);
            Assert.All(Masses(belt), mass => Assert.True(mass <= 2.0 + 1e-9));
        }

        Assert.Equal(10.0, belt.MassHeld, 9);
        Assert.Equal(0.0, belt.AcceptMass(belt.In), 9);
        Assert.Equal(new[] { 2.0, 2.0, 2.0, 2.0, 2.0 }, Masses(belt).Select(m => Math.Round(m, 9)));
    }

    [Fact]
    public void SpeedOutsideTheDeclaredRangeThrows()
    {
        BulkBelt belt = NewBelt(maxSpeed: 1.0);
        OutputPort<double> speed = Drive(belt, 1.5);

        Assert.Throws<InvalidOperationException>(() => belt.Advance(Dt));

        speed.Value = -1.0;
        Assert.Throws<InvalidOperationException>(() => belt.OfferMass(belt.Out));
    }

    [Fact]
    public void LinearDensityAtReadsTheCellUnderThePosition()
    {
        BulkBelt belt = NewBelt();
        Deposit(belt, 1.0);

        Assert.Equal(2.0, belt.LinearDensityAt(0.1), 9);
        Assert.Equal(0.0, belt.LinearDensityAt(2.4), 9);
        Assert.Equal(0.0, belt.LinearDensityAt(99.0), 9);
    }
}
