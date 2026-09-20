using Dse.Core.Catalogue;

namespace Dse.Components.Mechanical;

/// <summary><see cref="MotorRating"/> as catalogue parameters, shared by the motor and the conveyor.</summary>
public static class MotorRatingGroup
{
    public static GroupDefinition Definition { get; } = new(
        "MotorRating",
        Param.Double("ratedPowerW", "Nameplate power.", "W", min: 0.0, exclusiveMin: true),
        Param.Double("ratedSpeedRadPerS", "Shaft speed at rated load.", "rad/s", min: 0.0, exclusiveMin: true),
        Param.Double("ratedCurrentA", "Current at rated load.", "A", min: 0.0, exclusiveMin: true),
        Param.Double("noLoadCurrentFraction", "Current at zero torque, as a fraction of rated.", @default: 0.3, min: 0.0),
        Param.Double("lockedRotorCurrentMultiple", "Starting current, as a multiple of rated.", @default: 6.0, min: 0.0),
        Param.Double("breakdownTorqueMultiple", "Torque above which the motor stalls, as a multiple of rated.", @default: 2.5, min: 0.0),
        Param.Double("accelerationTimeConstantS", "First-order approach to target speed when energised.", "s", @default: 1.0, min: 0.0),
        Param.Double("coastTimeConstantS", "First-order decay to rest when de-energised.", "s", @default: 3.0, min: 0.0),
        Param.Double("thermalTimeConstantS", "Time constant of the I²t thermal state.", "s", @default: 60.0, min: 0.0),
        Param.Double("speedDroopFraction", "Speed lost at rated torque, as a fraction of rated speed.", @default: 0.03, min: 0.0));

    public static MotorRating Read(ParameterValues p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new MotorRating(
            p.Double("ratedPowerW"),
            p.Double("ratedSpeedRadPerS"),
            p.Double("ratedCurrentA"),
            p.Double("noLoadCurrentFraction"),
            p.Double("lockedRotorCurrentMultiple"),
            p.Double("breakdownTorqueMultiple"),
            p.Double("accelerationTimeConstantS"),
            p.Double("coastTimeConstantS"),
            p.Double("thermalTimeConstantS"),
            p.Double("speedDroopFraction"));
    }
}
