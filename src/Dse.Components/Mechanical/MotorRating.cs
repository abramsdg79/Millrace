namespace Dse.Components.Mechanical;

/// <summary>An induction motor's nameplate plus the handful of constants the model needs.</summary>
/// <param name="RatedPowerW">W.</param>
/// <param name="RatedSpeedRadPerS">rad/s at rated load.</param>
/// <param name="RatedCurrentA">A at rated load.</param>
/// <param name="NoLoadCurrentFraction">Current at zero torque as a fraction of rated.</param>
/// <param name="LockedRotorCurrentMultiple">Starting current as a multiple of rated.</param>
/// <param name="BreakdownTorqueMultiple">Torque above which the motor stalls, as a multiple of rated.</param>
/// <param name="AccelerationTimeConstantS">s; first-order approach to target speed when energised.</param>
/// <param name="CoastTimeConstantS">s; first-order decay to rest when de-energised.</param>
/// <param name="ThermalTimeConstantS">s; the I²t thermal state's time constant.</param>
/// <param name="SpeedDroopFraction">Speed lost at rated torque as a fraction of rated speed.</param>
public sealed record MotorRating(
    double RatedPowerW,
    double RatedSpeedRadPerS,
    double RatedCurrentA,
    double NoLoadCurrentFraction = 0.3,
    double LockedRotorCurrentMultiple = 6.0,
    double BreakdownTorqueMultiple = 2.5,
    double AccelerationTimeConstantS = 1.0,
    double CoastTimeConstantS = 3.0,
    double ThermalTimeConstantS = 60.0,
    double SpeedDroopFraction = 0.03)
{
    /// <summary>N·m at rated power and speed.</summary>
    public double RatedTorque => RatedPowerW / RatedSpeedRadPerS;
}
