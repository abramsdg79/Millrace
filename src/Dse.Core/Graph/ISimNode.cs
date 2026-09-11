namespace Dse.Core.Graph;

/// <summary>Anything the builder accepts: a leaf component or a composite.</summary>
public interface ISimNode
{
    string Id { get; }
}
