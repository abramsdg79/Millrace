namespace Millrace.Core.Catalogue;

/// <summary>One assembly's contribution to a catalogue. Needs a public parameterless constructor to be loaded by the CLI.</summary>
public interface ICatalogueModule
{
    /// <summary>Shown in duplicate-name errors and in the export.</summary>
    string Name { get; }

    void Register(CatalogueBuilder builder);
}
