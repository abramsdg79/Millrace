namespace Millrace.Components.Mechanical;

/// <summary>
/// Belt width and the material's angle of repose give the most a belt can
/// carry per metre. Exceeding it is what defines overload and spillage, so
/// the number is derived, never asserted.
/// </summary>
public static class BeltGeometry
{
    /// <summary>
    /// kg/m for a flat belt carrying a triangular surcharge: effective width
    /// <c>b = 0.9 W − 0.05</c> (the CEMA edge allowance), cross-section
    /// <c>b² tan θ / 4</c>, times bulk density.
    /// </summary>
    public static double MaxLinearDensity(double beltWidthM, double angleOfReposeDeg, double bulkDensityKgM3)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(beltWidthM);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bulkDensityKgM3);
        if (angleOfReposeDeg <= 0.0 || angleOfReposeDeg >= 90.0)
        {
            throw new ArgumentOutOfRangeException(nameof(angleOfReposeDeg), angleOfReposeDeg, "Angle of repose must be in (0, 90) degrees.");
        }

        double effectiveWidth = (0.9 * beltWidthM) - 0.05;
        if (effectiveWidth <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(beltWidthM), beltWidthM, "Belt is too narrow to carry a surcharge.");
        }

        double area = effectiveWidth * effectiveWidth * Math.Tan(angleOfReposeDeg * Math.PI / 180.0) / 4.0;
        return area * bulkDensityKgM3;
    }
}
