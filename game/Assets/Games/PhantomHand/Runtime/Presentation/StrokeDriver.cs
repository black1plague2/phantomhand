using System.Collections.Generic;
using Opus.Sdk;

namespace Opus.Games.PhantomHand.Presentation
{
    /// <summary>
    /// Connects the stroke plan to the world (U3 item 3). At induction start every planned stroke is scheduled on the sleeve
    /// (HapticClient.ScheduleStroke(motor, cueTime, tactile_lead_ms), motor 0 = A, 1 = B; U1 sends it at cue - lead). The brush
    /// reports the MEASURED pass times and the haptic client the actual cue SENDS (HapticCueRecord.SentMs); once a stroke has both
    /// passes and both cues resolved it is submitted to the module as a stroke event. For SYNC the module computes
    /// timing_err_ms = send + lead - measured pass. OLED: "SYNC"/"ASYNC" at the start, "IDLE" at the end. Plain C#, no Unity types.
    /// </summary>
    public sealed class StrokeDriver
    {
        private sealed class Rec
        {
            public StrokePlan Plan;
            public double? PassA, PassB, Send0, Send1;
            public bool Resolved0, Resolved1, Submitted;
        }

        private readonly PhantomHandModule _module;
        private readonly HapticClient _haptic;
        private readonly BrushRig _brush;
        private readonly Dictionary<string, KeyValuePair<int, int>> _cues = new Dictionary<string, KeyValuePair<int, int>>();
        private readonly Dictionary<int, Rec> _recs = new Dictionary<int, Rec>();
        private bool _active;

        public int Submitted { get; private set; }
        public int Planned { get { return _recs.Count; } }
        public bool Active { get { return _active; } }

        public StrokeDriver(PhantomHandModule module, HapticClient haptic, BrushRig brush)
        {
            _module = module; _haptic = haptic; _brush = brush;
        }

        public void Begin(double nowMs)
        {
            End(nowMs, false);
            var plan = _module.CurrentStrokes;
            var p = _module.Params;
            _recs.Clear(); _cues.Clear(); Submitted = 0;
            if (_haptic != null)
            {
                _haptic.Enabled = p.HapticsEnabled;
                _haptic.MaxIntensity = p.HapticMaxIntensity;
                _haptic.OnCueRecorded += OnCue;
            }
            foreach (var s in plan)
            {
                var r = new Rec { Plan = s };
                _recs[s.Index] = r;
                if (_haptic == null) { r.Resolved0 = r.Resolved1 = true; continue; }
                string id0 = _haptic.ScheduleStroke(0, s.CueMotor0Ms, p.TactileLeadMs);
                string id1 = _haptic.ScheduleStroke(1, s.CueMotor1Ms, p.TactileLeadMs);
                _cues[id0] = new KeyValuePair<int, int>(s.Index, 0);
                _cues[id1] = new KeyValuePair<int, int>(s.Index, 1);
            }
            if (_brush != null)
            {
                _brush.OnPass += OnPass;
                _brush.SetPlan(plan);
                _brush.Active = true;
            }
            if (_haptic != null)
                _haptic.SendDisplay(_module.CurrentCondition == PhCondition.Sync ? "SYNC" : "ASYNC", nowMs);
            _active = true;
        }

        /// <summary>Call after the haptic client's Pump and the brush's Tick.</summary>
        public void Tick(double nowMs)
        {
            if (!_active) return;
            foreach (var r in _recs.Values)
            {
                if (r.Submitted) continue;
                bool ready = r.PassA.HasValue && r.PassB.HasValue && r.Resolved0 && r.Resolved1;
                if (!ready && nowMs > System.Math.Max(r.Plan.EndMs, r.Plan.LastCueMs) + 500 && r.PassB.HasValue) ready = true;
                if (ready) Submit(r);
            }
        }

        /// <summary>Ends the induction: unsent strokes are cancelled, finished ones are flushed, the OLED shows IDLE.</summary>
        public void End(double nowMs, bool showIdle = true)
        {
            if (!_active) return;
            if (_haptic != null)
            {
                _haptic.CancelPendingStrokes();
                _haptic.OnCueRecorded -= OnCue;
            }
            if (_brush != null) _brush.OnPass -= OnPass;
            foreach (var r in _recs.Values) if (!r.Submitted && r.PassA.HasValue && r.PassB.HasValue) Submit(r);
            if (_haptic != null && showIdle) _haptic.SendDisplay("IDLE", nowMs);
            _active = false;
        }

        private void OnPass(int stroke, int slot, double ms)
        {
            Rec r;
            if (!_recs.TryGetValue(stroke, out r)) return;
            if (slot == 0) r.PassA = ms; else r.PassB = ms;
        }

        private void OnCue(HapticCueRecord rec)
        {
            KeyValuePair<int, int> key;
            if (rec.Cue != "stroke" || rec.CueId == null || !_cues.TryGetValue(rec.CueId, out key)) return;
            Rec r;
            if (!_recs.TryGetValue(key.Key, out r)) return;
            if (key.Value == 0) { r.Send0 = rec.SentMs; r.Resolved0 = true; }
            else { r.Send1 = rec.SentMs; r.Resolved1 = true; }
        }

        private void Submit(Rec r)
        {
            r.Submitted = true;
            if (_module.SubmitStroke(new StrokeRecord
            {
                Index = r.Plan.Index,
                PassAMs = r.PassA ?? r.Plan.PassAMs,
                PassBMs = r.PassB ?? r.Plan.PassBMs,
                CueASendMs = r.Send0,
                CueBSendMs = r.Send1,
                Swapped = r.Plan.Swapped,
            })) Submitted++;
        }
    }
}
