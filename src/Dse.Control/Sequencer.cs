using System.Globalization;
using Dse.Io;

namespace Dse.Control;

/// <summary>
/// A linear sequence. <c>Start</c> from idle enters step 1; each step commands
/// its entry writes on the scan that enters it and is then tested, one scan
/// later, against its transition and its timeout. <c>Hold</c> freezes the step
/// clock, <c>Resume</c> continues, <c>Abort</c> returns to idle with the abort
/// writes, and <c>Reset</c> returns to idle from <c>Faulted</c> or
/// <c>Complete</c>. Every command is rising-edge sensitive, so a tag left high
/// does not retrigger. A timeout fault drives the abort writes, so the plant
/// reaches the same safe state as an operator abort.
/// </summary>
public sealed class Sequencer : IScanBlock
{
    private const int StartCommand = 0;
    private const int HoldCommand = 1;
    private const int ResumeCommand = 2;
    private const int AbortCommand = 3;
    private const int ResetCommand = 4;

    private readonly SequenceStep[] _steps;
    private readonly BlockWrite[] _abortWrites;
    private readonly int[] _predicateInput;      // per step: the input index its predicate reads, or -1
    private readonly int[][] _entryWriteIndex;   // per step: the write index of each entry write
    private readonly int[] _abortWriteIndex;
    private readonly bool[] _previous = new bool[5];

    private int _step;                           // 0 = idle
    private bool _running;
    private bool _held;
    private bool _complete;
    private bool _faulted;
    private double _stepTime;

    /// <summary>Creates a sequencer.</summary>
    /// <param name="id">The block id; prefixes every owned tag.</param>
    /// <param name="steps">At least one step, in order.</param>
    /// <param name="scanPeriod">How often the block scans.</param>
    /// <param name="abortWrites">What to command when the sequence is aborted; may be null or empty.</param>
    public Sequencer(
        string id,
        IReadOnlyList<SequenceStep> steps,
        TimeSpan scanPeriod,
        IReadOnlyList<BlockWrite>? abortWrites = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Count == 0)
        {
            throw new ArgumentException("A sequence needs at least one step.", nameof(steps));
        }

        if (scanPeriod <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scanPeriod), scanPeriod, "The scan period must be positive.");
        }

        _steps = [.. steps];
        _abortWrites = abortWrites is null ? [] : [.. abortWrites];

        var inputs = new List<TagRef>();
        var writes = new List<TagRef>();
        _predicateInput = new int[_steps.Length];
        _entryWriteIndex = new int[_steps.Length][];

        for (int i = 0; i < _steps.Length; i++)
        {
            SequenceStep step = _steps[i];
            _predicateInput[i] = step.Transition.IsTimed
                ? -1
                : Pin(inputs, new TagRef(step.Transition.Tag!, step.Transition.Value.Kind), "read", nameof(steps));

            int[] indices = new int[step.EntryWrites.Count];
            for (int w = 0; w < indices.Length; w++)
            {
                BlockWrite write = step.EntryWrites[w];
                ArgumentException.ThrowIfNullOrWhiteSpace(write.Tag, nameof(steps));
                indices[w] = Pin(writes, new TagRef(write.Tag, write.Value.Kind), "commanded", nameof(steps));
            }

            _entryWriteIndex[i] = indices;
        }

        _abortWriteIndex = new int[_abortWrites.Length];
        for (int w = 0; w < _abortWrites.Length; w++)
        {
            BlockWrite write = _abortWrites[w];
            ArgumentException.ThrowIfNullOrWhiteSpace(write.Tag, nameof(abortWrites));
            _abortWriteIndex[w] = Pin(writes, new TagRef(write.Tag, write.Value.Kind), "commanded", nameof(abortWrites));
        }

        Id = id;
        ScanPeriod = scanPeriod;
        Inputs = inputs;
        Writes = writes;
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <inheritdoc/>
    public TimeSpan ScanPeriod { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Inputs { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagRef> Writes { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Outputs { get; } =
    [
        new TagSpec("Step", TagKind.Int64, "", "The step running, or 0 when idle"),
        new TagSpec("Running", TagKind.Bool, "", "A step is running"),
        new TagSpec("Held", TagKind.Bool, "", "The step clock is frozen"),
        new TagSpec("Complete", TagKind.Bool, "", "The last step finished"),
        new TagSpec("Faulted", TagKind.Bool, "", "A step timed out"),
        new TagSpec("StepTime", TagKind.Double, "s", "Time in the current step"),
    ];

    /// <inheritdoc/>
    public IReadOnlyList<TagSpec> Commands { get; } =
    [
        new TagSpec("Start", TagKind.Bool, "", "Enters step 1 from idle on a rising edge"),
        new TagSpec("Hold", TagKind.Bool, "", "Freezes the step clock on a rising edge"),
        new TagSpec("Resume", TagKind.Bool, "", "Continues a held step on a rising edge"),
        new TagSpec("Abort", TagKind.Bool, "", "Returns to idle on a rising edge"),
        new TagSpec("Reset", TagKind.Bool, "", "Returns to idle from faulted or complete on a rising edge"),
    ];

    /// <inheritdoc/>
    public void Scan(in ScanInputs inputs, ref ScanOutputs outputs)
    {
        Span<bool> edges = stackalloc bool[5];
        for (int i = 0; i < 5; i++)
        {
            bool value = inputs.Command(i).AsBool;
            edges[i] = value && !_previous[i];
            _previous[i] = value;
        }

        if (edges[AbortCommand] && _running)
        {
            outputs.Raise("SEQUENCE_ABORTED",
                string.Create(CultureInfo.InvariantCulture, $"Aborted at step {_step}."));
            Abort(ref outputs);
            GoIdle();
        }
        else if (edges[ResetCommand] && (_faulted || _complete))
        {
            _faulted = false;
            _complete = false;
            GoIdle();
        }
        else if (edges[StartCommand] && !_running && !_faulted && !_complete)
        {
            Enter(1, ref outputs);
        }
        else if (edges[HoldCommand] && _running)
        {
            _held = true;
        }
        else if (edges[ResumeCommand] && _held)
        {
            _held = false;
        }
        else if (_running && !_held)
        {
            _stepTime += inputs.Elapsed;
            SequenceStep step = _steps[_step - 1];

            if (Satisfied(step, _predicateInput[_step - 1], in inputs))
            {
                if (_step == _steps.Length)
                {
                    outputs.Raise("SEQUENCE_COMPLETE", string.Create(
                        CultureInfo.InvariantCulture,
                        $"Finished after {_steps.Length} step{(_steps.Length == 1 ? "" : "s")}."));
                    _complete = true;
                    GoIdle();
                }
                else
                {
                    Enter(_step + 1, ref outputs);
                }
            }
            else if (step.Timeout is { } limit && _stepTime >= limit.TotalSeconds)
            {
                outputs.Raise("SEQUENCE_FAULTED", string.Create(CultureInfo.InvariantCulture,
                    $"Step {_step} timed out after {limit.TotalSeconds} s."));
                Abort(ref outputs);
                _faulted = true;
                _running = false;
                _held = false;
            }
        }

        outputs.Set(0, TagValue.Int64(_step));
        outputs.Set(1, TagValue.Bool(_running));
        outputs.Set(2, TagValue.Bool(_held));
        outputs.Set(3, TagValue.Bool(_complete));
        outputs.Set(4, TagValue.Bool(_faulted));
        outputs.Set(5, TagValue.Double(_stepTime));
    }

    /// <summary>Adds a pin if it is new, and returns its index. Two pins on one tag must agree on the kind.</summary>
    private static int Pin(List<TagRef> pins, TagRef pin, string verb, string parameter)
    {
        for (int i = 0; i < pins.Count; i++)
        {
            if (!string.Equals(pins[i].Name, pin.Name, StringComparison.Ordinal))
            {
                continue;
            }

            if (pins[i].Kind != pin.Kind)
            {
                throw new ArgumentException(
                    $"Tag '{pin.Name}' is {verb} as a {pins[i].Kind} and as a {pin.Kind}. Use one kind.",
                    parameter);
            }

            return i;
        }

        pins.Add(pin);
        return pins.Count - 1;
    }

    private bool Satisfied(SequenceStep step, int inputIndex, in ScanInputs inputs)
    {
        if (step.Transition.IsTimed)
        {
            return _stepTime >= step.Transition.Delay.TotalSeconds;
        }

        TagValue actual = inputs.Input(inputIndex);
        TagValue wanted = step.Transition.Value;

        if (actual.Kind == TagKind.Bool)
        {
            bool equal = actual.AsBool == wanted.AsBool;
            return step.Transition.Operator == PredicateOperator.Equal ? equal : !equal;
        }

        double left = actual.Kind == TagKind.Double ? actual.AsDouble : actual.AsInt64;
        double right = wanted.Kind == TagKind.Double ? wanted.AsDouble : wanted.AsInt64;

        return step.Transition.Operator switch
        {
            PredicateOperator.Equal => left == right,
            PredicateOperator.NotEqual => left != right,
            PredicateOperator.Less => left < right,
            PredicateOperator.LessOrEqual => left <= right,
            PredicateOperator.Greater => left > right,
            _ => left >= right,
        };
    }

    /// <summary>
    /// Commands every abort write. Both the <c>Abort</c> command and a step
    /// timeout drive them, so a fault leaves the plant where an abort would.
    /// </summary>
    private void Abort(ref ScanOutputs outputs)
    {
        for (int w = 0; w < _abortWrites.Length; w++)
        {
            outputs.Write(_abortWriteIndex[w], _abortWrites[w].Value);
        }
    }

    private void Enter(int step, ref ScanOutputs outputs)
    {
        _step = step;
        _running = true;
        _held = false;
        _stepTime = 0.0;

        SequenceStep entered = _steps[step - 1];
        int[] indices = _entryWriteIndex[step - 1];
        for (int w = 0; w < indices.Length; w++)
        {
            outputs.Write(indices[w], entered.EntryWrites[w].Value);
        }

        outputs.Raise("STEP_ENTERED",
            string.Create(CultureInfo.InvariantCulture, $"{step}: {entered.Name}."));
    }

    private void GoIdle()
    {
        _step = 0;
        _running = false;
        _held = false;
        _stepTime = 0.0;
    }
}
