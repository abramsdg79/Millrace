namespace Dse.Core.Io;

/// <summary>
/// A component that publishes tags (spec 9.1). Names are relative to the
/// component; the builder prefixes them with the component id, and a composite
/// that exposes the same port under an alias renames the tag to
/// <c>CompositeId.Alias</c>. Declare what a plant measures or commands; leave
/// the god view to telemetry.
/// </summary>
public interface ITagProvider
{
    /// <summary>The component's bindings. Called once, at <c>Build()</c>.</summary>
    IEnumerable<TagBinding> DescribeTags();
}
