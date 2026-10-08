using Newtonsoft.Json.Linq;
using Opus.Games.PhantomHand;

namespace Opus.Shell
{
    /// <summary>Outcome of one Phantom Hand operator command.</summary>
    public sealed class PhantomCommandResult
    {
        public bool Handled;           // the command name is one of ours
        public string Error;           // null = ack ok
        public bool StopMotors;        // abort_phase: the caller cancels queued strokes and stops the sleeve
        public string PendingOrder;    // set_condition_order accepted for the NEXT run (no run active)
        public string LogPhase;        // phase the command acted on (for the command_received event)
    }

    /// <summary>
    /// phase_next / abort_phase / set_condition_order (contracts/LIVE_PROTOCOL.md v0.2). Plain C# over a <see cref="PhantomHandModule"/>
    /// so the rules are EditMode-tested; the scene controller applies the side effects (motors off, event log).
    ///  - phase_next: skip to the next phase of an active run (ends an induction early). With no active run it is ignored (ack ok).
    ///  - abort_phase: abort the current phase (the module records it unconfirmed) and go on; the caller stops the motors and hides stimuli.
    ///  - set_condition_order: params.condition_order sync_first|async_first (required). Before the first induction of an active run it is
    ///    applied to that run; with no active run it is kept for the next run; once the first induction has started it is rejected.
    /// </summary>
    public static class PhantomCommandRouter
    {
        public static PhantomCommandResult Handle(string command, JObject p, PhantomHandModule module, bool runActive)
        {
            var r = new PhantomCommandResult();
            bool active = runActive && module != null && module.IsRunning;
            switch (command)
            {
                case "phase_next":
                    r.Handled = true;
                    if (!active) return r; // ignored when no run is active
                    r.LogPhase = PhNames.Of(module.CurrentPhase);
                    if (!module.AdvancePhase()) r.Error = "cannot advance from phase '" + r.LogPhase + "'";
                    return r;

                case "abort_phase":
                    r.Handled = true;
                    if (!active) return r;
                    r.LogPhase = PhNames.Of(module.CurrentPhase);
                    r.StopMotors = true;
                    if (!module.AbortPhase()) r.Error = "cannot abort phase '" + r.LogPhase + "'";
                    return r;

                case "set_condition_order":
                    r.Handled = true;
                    string order = p != null && p["condition_order"] != null && p["condition_order"].Type == JTokenType.String
                        ? p["condition_order"].Value<string>() : null;
                    if (order != "sync_first" && order != "async_first")
                    {
                        r.Error = "params.condition_order must be 'sync_first' or 'async_first'";
                        return r;
                    }
                    if (active)
                    {
                        if (!module.SetConditionOrder(order)) r.Error = "condition order is locked: the first induction has already started";
                        else r.PendingOrder = order; // also the default for the next person
                    }
                    else r.PendingOrder = order;
                    return r;
            }
            return r; // Handled = false
        }
    }
}
