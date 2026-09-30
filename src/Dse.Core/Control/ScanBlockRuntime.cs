using Dse.Core.Io;
using Dse.Core.Logging;
using Dse.Io;

namespace Dse.Core.Control;

/// <summary>
/// One block, running. Resolves its pins to image indices once, then does the
/// same five things on every scan: read the published image, call
/// <see cref="IScanBlock.Scan"/>, store the outputs into the block's own ports,
/// queue the writes, and log the events under the block's id.
/// </summary>
/// <remarks>
/// <para>
/// Every buffer is allocated here, once. A scan that raises no event allocates
/// nothing (R68).
/// </para>
/// <para>
/// Two blocks may name the same tag in their <see cref="IScanBlock.Writes"/>.
/// Both writes are queued and applied at phase 1 of the next tick in enqueue
/// order, and blocks scan in the order they were added, so the block added
/// later wins. That is deterministic and it is exactly what a PLC does with a
/// double coil — but, as on a PLC, it is usually a mistake, and a claimed tag
/// (DSE016) has exactly one writer. Every block write
/// is logged <c>Set to … by &lt;block id&gt;.</c> and none reaches the action
/// recorder.
/// </para>
/// </remarks>
internal sealed class ScanBlockRuntime
{
    private readonly ScanBlockPlan _plan;
    private readonly TagImage _io;
    private readonly EventLog _log;
    private readonly int[] _inputIndices;
    private readonly int[] _writeIndices;
    private readonly int[] _commandIndices;
    private readonly TagValue[] _inputValues;
    private readonly TagValue[] _commandValues;
    private ScanOutputs _outputs;
    private long _lastScanTick = -1L;

    internal ScanBlockRuntime(ScanBlockPlan plan, TagImage io, EventLog log)
    {
        _plan = plan;
        _io = io;
        _log = log;

        _inputIndices = Indices(io, plan.Block.Inputs);
        _writeIndices = Indices(io, plan.Block.Writes);
        _commandIndices = new int[plan.Commands.Length];
        for (int i = 0; i < _commandIndices.Length; i++)
        {
            _commandIndices[i] = io.Directory.Find(plan.Commands[i].Name).Index;
        }

        _inputValues = new TagValue[_inputIndices.Length];
        _commandValues = new TagValue[_commandIndices.Length];
        _outputs = new ScanOutputs(plan.Outputs.Length, _writeIndices.Length);
    }

    /// <summary>The scan period in ticks; at least one.</summary>
    internal long PeriodTicks => _plan.PeriodTicks;

    /// <summary>One scan, during phase 1 of <paramref name="tick"/>.</summary>
    internal void Scan(long tick, DateTimeOffset now, double deltaSeconds)
    {
        for (int i = 0; i < _inputIndices.Length; i++)
        {
            _inputValues[i] = _io.Read(_inputIndices[i]);
        }

        for (int i = 0; i < _commandIndices.Length; i++)
        {
            _commandValues[i] = _io.Read(_commandIndices[i]);
        }

        double elapsed = _lastScanTick < 0L ? 0.0 : (tick - _lastScanTick) * deltaSeconds;
        var inputs = new ScanInputs(_inputValues, _commandValues, tick, now, deltaSeconds, elapsed);

        _outputs.Reset();
        ScanOutputs outputs = _outputs;
        _plan.Block.Scan(in inputs, ref outputs);
        _lastScanTick = tick;

        for (int i = 0; i < _plan.Outputs.Length; i++)
        {
            if (_outputs.TryOutput(i, out TagValue value))
            {
                OwnedTag output = _plan.Outputs[i];
                if (value.Kind != output.Binding.Kind)
                {
                    throw new InvalidOperationException(
                        $"Block '{_plan.Block.Id}' set output '{output.Name}' to a {value.Kind}, but that pin " +
                        $"was declared as a {output.Binding.Kind}. Set the pin with the kind it declares.");
                }

                output.Store(value);
            }
        }

        for (int i = 0; i < _writeIndices.Length; i++)
        {
            if (_outputs.TryWrite(i, out TagValue value))
            {
                _io.Write(_writeIndices[i], value, _plan.Block.Id);
            }
        }

        IReadOnlyList<BlockEvent> events = _outputs.Events;
        for (int i = 0; i < events.Count; i++)
        {
            _log.Record(tick, now, _plan.Block.Id, events[i].Code, events[i].Message);
        }
    }

    private static int[] Indices(TagImage io, IReadOnlyList<TagRef> pins)
    {
        var indices = new int[pins.Count];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = io.Directory.Find(pins[i].Name).Index;
        }

        return indices;
    }
}
