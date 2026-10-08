namespace Millrace.Core.Flow;

/// <summary>A node that discharges bulk material through one or more outlets.</summary>
public interface IBulkProducer
{
    /// <summary>Kilograms this node wants to push through <paramref name="outlet"/> this tick.</summary>
    double OfferMass(FlowOutlet outlet);

    /// <summary>Removes up to <paramref name="mass"/> kilograms from behind <paramref name="outlet"/> and returns them.</summary>
    BulkLot Withdraw(FlowOutlet outlet, double mass);
}
