using Dse.Core.Catalogue;

namespace Dse.Control.Catalogue;

/// <summary>
/// The six control blocks of <c>Dse.Control</c> and the sequencer's
/// <c>transition</c> slot, for a plant file's <c>controllers</c> section.
/// </summary>
public sealed class ControlModule : ICatalogueModule
{
    public string Name => "Dse.Control";

    public void Register(CatalogueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Blocks
        builder.AddBlock(TimerCatalogue.Descriptor);
        builder.AddBlock(PermissiveCatalogue.Descriptor);
        builder.AddBlock(InterlockCatalogue.Descriptor);
        builder.AddBlock(AlarmCatalogue.Descriptor);
        builder.AddBlock(SequencerCatalogue.Descriptor);
        builder.AddBlock(CoilCatalogue.Descriptor);

        // Transitions
        builder.Add(TransitionCatalogue.After);
        builder.Add(TransitionCatalogue.When);
    }
}
