namespace Millrace.Core.Flow;

/// <summary>
/// Something that happens to material while it is resident in a node: heat
/// transfer, moisture loss, time above a threshold. Applied once per tick to
/// every parcel the node holds. The state parameter is the parcel's
/// accumulated-state array, sized by its material's schema — empty for bulk
/// in this version and for any material with no schema.
/// </summary>
public interface IMaterialTransform
{
    void Apply(ref MaterialProperties properties, Span<double> state, double dt, in TransformContext context);
}
