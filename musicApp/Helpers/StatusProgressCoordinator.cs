using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;

namespace musicApp.Helpers
{
    /// <summary>
    /// Single owner of the status-bar progress UI. Background work registers a Phase
    /// (optionally with weighted stages); only the highest-priority active phase renders,
    /// and the rendered fill fraction never moves backwards within a phase. When the last
    /// phase ends the bar hides and the idle status text is restored.
    /// </summary>
    public sealed class StatusProgressCoordinator
    {
        private readonly Dispatcher _dispatcher;
        private readonly Func<string> _textPrefix;
        private readonly Action<string> _setText;
        private readonly Action<double> _showBarFraction;
        private readonly Action _hideBarAndRestoreIdle;
        private readonly List<Phase> _phases = new();

        public StatusProgressCoordinator(
            Dispatcher dispatcher,
            Func<string> textPrefix,
            Action<string> setText,
            Action<double> showBarFraction,
            Action hideBarAndRestoreIdle)
        {
            _dispatcher = dispatcher;
            _textPrefix = textPrefix;
            _setText = setText;
            _showBarFraction = showBarFraction;
            _hideBarAndRestoreIdle = hideBarAndRestoreIdle;
        }

        public bool IsActive
        {
            get { lock (_phases) return _phases.Count > 0; }
        }

        public Phase Begin(int priority, params (string label, double weight)[] stages)
        {
            if (stages == null || stages.Length == 0)
                stages = new[] { ("working", 1.0) };
            var phase = new Phase(this, priority, stages);
            lock (_phases)
                _phases.Add(phase);
            Render();
            return phase;
        }

        private void End(Phase phase)
        {
            lock (_phases)
                _phases.Remove(phase);
            Render();
        }

        private void Render()
        {
            _dispatcher.BeginInvoke(() =>
            {
                Phase? top;
                lock (_phases)
                    top = _phases.OrderByDescending(p => p.Priority).FirstOrDefault();

                if (top == null)
                {
                    _hideBarAndRestoreIdle();
                    return;
                }

                var (label, done, total, fraction) = top.Snapshot();
                _showBarFraction(fraction);
                var counts = total > 0 ? $" {done}/{total}" : "";
                _setText($"{_textPrefix()}, {label}{counts}…");
            }, DispatcherPriority.Normal);
        }

        public sealed class Phase : IDisposable
        {
            private readonly StatusProgressCoordinator _owner;
            private readonly (string label, double weight)[] _stages;
            private readonly object _sync = new();
            private int _stage;
            private int _done;
            private int _total;
            private double _renderedFraction;
            private bool _ended;

            internal Phase(StatusProgressCoordinator owner, int priority, (string label, double weight)[] stages)
            {
                _owner = owner;
                Priority = priority;
                _stages = stages;
            }

            public int Priority { get; }
            public int StageCount => _stages.Length;

            public void Report(int stage, int done, int total)
            {
                lock (_sync)
                {
                    if (_ended)
                        return;
                    stage = Math.Clamp(stage, 0, _stages.Length - 1);
                    double before = 0;
                    for (int i = 0; i < stage; i++)
                        before += _stages[i].weight;
                    var inStage = total > 0 ? Math.Clamp(done / (double)total, 0, 1) : 0;
                    var fraction = before + _stages[stage].weight * inStage;
                    // never move the bar backwards within a phase
                    _renderedFraction = Math.Max(_renderedFraction, Math.Min(fraction, 1.0));
                    _stage = stage;
                    _done = done;
                    _total = total;
                }
                _owner.Render();
            }

            internal (string label, int done, int total, double fraction) Snapshot()
            {
                lock (_sync)
                    return (_stages[_stage].label, _done, _total, _renderedFraction);
            }

            public void Dispose()
            {
                lock (_sync)
                {
                    if (_ended)
                        return;
                    _ended = true;
                }
                _owner.End(this);
            }
        }
    }
}
