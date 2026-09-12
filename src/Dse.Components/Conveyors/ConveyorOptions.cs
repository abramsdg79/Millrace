using Dse.Components.Mechanical;

namespace Dse.Components.Conveyors;

/// <summary>Everything a bulk conveyor is built from.</summary>
/// <param name="LengthM">m.</param>
/// <param name="CellSizeM">m; must divide the length and satisfy the CFL condition.</param>
/// <param name="BeltWidthM">m.</param>
/// <param name="AngleOfReposeDeg">Degrees, of the material carried.</param>
/// <param name="MaterialDensityKgM3">kg/m³, of the material carried.</param>
/// <param name="EmptyBeltMassKg">kg of belt and moving parts.</param>
/// <param name="FrictionCoefficient">Rolling friction, dimensionless.</param>
/// <param name="PulleyDiameterM">m, drive pulley.</param>
/// <param name="GearRatio">Motor turns per pulley turn.</param>
/// <param name="Motor">The motor's rating.</param>
/// <param name="TailDragN">N, tail pulley bearing drag.</param>
/// <param name="PullKeys">Number of pull-wire switches along the belt.</param>
/// <param name="SpeedMarginFraction">How far the belt's declared max speed exceeds the no-load speed. Zero leaves no headroom: the belt throws if drive speed ever exceeds its declared maximum.</param>
public sealed record ConveyorOptions(
    double LengthM,
    double CellSizeM,
    double BeltWidthM,
    double AngleOfReposeDeg,
    double MaterialDensityKgM3,
    double EmptyBeltMassKg,
    double FrictionCoefficient,
    double PulleyDiameterM,
    double GearRatio,
    MotorRating Motor,
    double TailDragN = 50.0,
    int PullKeys = 2,
    double SpeedMarginFraction = 0.1);
