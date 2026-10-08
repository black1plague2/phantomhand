using Newtonsoft.Json.Linq;

namespace Opus.Shell
{
    /// <summary>
    /// Carries state across a scene change inside one app run (the Bootstrap scene -> the game scene, and Phantom Hand's
    /// "next person" reload). Static on purpose: scene objects do not survive the load, process memory does.
    /// </summary>
    public static class ProgramHandoff
    {
        /// <summary>An assign_program payload ({program, patient_ref}) for the next scene's session runner to apply once.
        /// Set by the Bootstrap scene (which already acked it) and by the runner when it reloads for the next person
        /// (so the new participant gets the same prescription and patient).</summary>
        public static JObject PendingProgram;

        /// <summary>Set by Phantom Hand's next_person: the reloaded scene starts a session on its own as soon as it is up.</summary>
        public static string PendingAutoStartReason;

        public static JObject TakeProgram()
        {
            var p = PendingProgram;
            PendingProgram = null;
            return p;
        }

        public static string TakeAutoStart()
        {
            var r = PendingAutoStartReason;
            PendingAutoStartReason = null;
            return r;
        }

        public static void Clear()
        {
            PendingProgram = null;
            PendingAutoStartReason = null;
        }
    }
}
