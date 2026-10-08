using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Opus.Games.PhantomHand.EditorTools
{
    /// <summary>Runs tests through TestRunnerApi and writes a COMPACT summary (counts + failures only) to
    /// logs/sessions/screens/ph/u2/testrun.txt. The MCP TestRunnerTools returns every result (100+ rows, too big for the bridge).
    /// Start("") = all EditMode tests (or a regex group filter); StartPlay("") = all PlayMode tests. PlayMode entry reloads the
    /// domain, so the callback is re-registered from [InitializeOnLoad] and the counters live in SessionState.</summary>
    [InitializeOnLoad]
    public static class CompactTestRun
    {
        private static readonly string OutPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../logs/sessions/screens/ph/u2/testrun.txt"));
        private const string KeyActive = "ph_ctr_active", KeyPass = "ph_ctr_pass", KeyFail = "ph_ctr_fail", KeySkip = "ph_ctr_skip", KeyText = "ph_ctr_text", KeyMode = "ph_ctr_mode", KeySeen = "ph_ctr_seen";
        private static TestRunnerApi _api;

        static CompactTestRun()
        {
            if (SessionState.GetBool(KeyActive, false)) Register();   // re-attach after a domain reload (PlayMode)
        }

        public static string Start(string groupFilter) { return Run(TestMode.EditMode, groupFilter); }
        public static string StartPlay(string groupFilter) { return Run(TestMode.PlayMode, groupFilter); }

        private static string Run(TestMode mode, string groupFilter)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, "RUNNING " + mode + " " + System.DateTime.Now.ToString("HH:mm:ss") + "\n");
            SessionState.SetBool(KeyActive, true);
            SessionState.SetInt(KeyPass, 0); SessionState.SetInt(KeyFail, 0); SessionState.SetInt(KeySkip, 0);
            SessionState.SetString(KeyText, ""); SessionState.SetString(KeyMode, mode.ToString()); SessionState.SetString(KeySeen, "");
            Register();
            var f = new Filter { testMode = mode };
            if (!string.IsNullOrEmpty(groupFilter)) f.groupNames = new[] { groupFilter };
            _api.Execute(new ExecutionSettings(f));
            return "started " + mode;
        }

        private static void Register()
        {
            if (_api == null) _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            _api.RegisterCallbacks(new Callbacks());
        }

        public static string Status() { return File.Exists(OutPath) ? File.ReadAllText(OutPath) : "none"; }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor r)
            {
                if (r.HasChildren) return;
                // the test runner can deliver one result to several registered callbacks after domain reloads: count each test once
                string seen = SessionState.GetString(KeySeen, "");
                string tag = "|" + r.Test.FullName + "|";
                if (seen.Contains(tag)) return;
                SessionState.SetString(KeySeen, seen + tag);
                switch (r.TestStatus)
                {
                    case TestStatus.Passed: SessionState.SetInt(KeyPass, SessionState.GetInt(KeyPass, 0) + 1); break;
                    case TestStatus.Skipped: case TestStatus.Inconclusive: SessionState.SetInt(KeySkip, SessionState.GetInt(KeySkip, 0) + 1); break;
                    default:
                        SessionState.SetInt(KeyFail, SessionState.GetInt(KeyFail, 0) + 1);
                        SessionState.SetString(KeyText, SessionState.GetString(KeyText, "") + "FAIL " + r.Test.FullName + " :: " + (r.Message ?? "").Replace("\n", " | ") + " @@ " + ((r.StackTrace ?? "").Split('\n')[0]) + "\n");
                        break;
                }
            }
            public void RunFinished(ITestResultAdaptor result)
            {
                if (!SessionState.GetBool(KeyActive, false)) return;
                int p = SessionState.GetInt(KeyPass, 0), f = SessionState.GetInt(KeyFail, 0), s = SessionState.GetInt(KeySkip, 0);
                File.WriteAllText(OutPath, "DONE " + SessionState.GetString(KeyMode, "?") + " passed=" + p + " failed=" + f + " skipped=" + s + " total=" + (p + f + s) + "\n" + SessionState.GetString(KeyText, ""));
                SessionState.SetBool(KeyActive, false);
            }
        }
    }
}
