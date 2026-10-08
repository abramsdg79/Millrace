namespace Millrace.Core.Graph;

/// <summary>Lets a composite prepend its id to everything it contains.</summary>
internal interface IQualifiable
{
    void Qualify(string prefix);
}
