using System.Globalization;

namespace Opus.Games.PhantomHand
{
    /// <summary>
    /// U4 item 5: what the in-headset HUD says, as plain data. Two non-disruptive chips ("Sleeve offline", "EMG offline",
    /// PRD section 11 fail-safe), the phase word and the time left. Spectator details (condition, EMG level, strokes) are
    /// hidden unless <see cref="SpectatorVisible"/> is set. The participant never sees the condition by default, so the
    /// HUD cannot bias the illusion.
    /// </summary>
    public sealed class HudModel
    {
        public string Lang = PhStrings.En;
        public PhPhase Phase = PhPhase.Idle;
        public PhCondition? Condition;
        public double RemainingS;
        public bool HapticConnected, BioConnected;
        public double EmgLevel01;
        public int StrokeCount;
        public bool SpectatorVisible;

        public bool SleeveOffline { get { return !HapticConnected; } }
        public bool EmgOffline { get { return !BioConnected; } }

        public string PhaseText { get { return PhStrings.PhaseWord(Phase, Lang); } }

        public string TimeLeftText
        {
            get
            {
                if (RemainingS <= 0 || Phase == PhPhase.Idle || Phase == PhPhase.Done || Phase == PhPhase.Witness) return "";
                int s = (int)System.Math.Ceiling(RemainingS);
                return PhStrings.Format("hud_time_left", Lang, (s / 60) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Spectator lines; empty when hidden.</summary>
        public string SpectatorText
        {
            get
            {
                if (!SpectatorVisible) return "";
                string cond = Condition.HasValue ? (Condition.Value == PhCondition.Sync ? "SYNC" : "ASYNC") : "-";
                string emg = BioConnected ? ((int)System.Math.Round(EmgLevel01 * 100)).ToString(CultureInfo.InvariantCulture) + "%" : "-";
                return PhStrings.Get("hud_condition", Lang) + ": " + cond + "\n" +
                       PhStrings.Get("hud_emg", Lang) + ": " + emg + "\n" +
                       PhStrings.Get("hud_strokes", Lang) + ": " + StrokeCount.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
