namespace Dse.Core.Flow;

/// <summary>One material connection, resolved to its two nodes at build time.</summary>
internal readonly record struct FlowLink(
    IFlowNode Producer,
    FlowOutlet Outlet,
    IFlowNode Consumer,
    FlowInlet Inlet);
