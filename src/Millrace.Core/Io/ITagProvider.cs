namespace Millrace.Core.Io;

/// <summary>
/// A component that publishes tags (spec 9.1). Names are relative to the
/// component; the builder prefixes them with the component id, and a composite
/// that exposes the same port under an alias renames the tag to
/// <c>CompositeId.Alias</c>. Declare what a plant measures or commands; leave
/// the god view to telemetry.
/// </summary>
public interface ITagProvider
{
    /// <summary>
    /// The component's bindings. Called by <c>Validate()</c>, <c>Build()</c> and
    /// <c>PlantTags()</c>, so possibly more than once: return fresh bindings over
    /// the same ports and change nothing.
    /// </summary>
    IEnumerable<TagBinding> DescribeTags();
}
