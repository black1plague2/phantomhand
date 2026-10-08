using System;
using UnityEngine;

namespace Opus.Shell
{
    /// <summary>
    /// Idempotent, never-throwing owner of one platform lock (here: the Android Wi-Fi multicast lock). Platform-independent so the
    /// rules are EditMode-testable: <see cref="Acquire"/> calls the platform once and remembers a failure (one warning, no retry
    /// storm), <see cref="Release"/> is safe to call at any time, and nothing the platform or the logger does can escape as an exception.
    /// </summary>
    public sealed class MulticastLockHolder
    {
        private readonly Func<bool> _acquire;
        private readonly Action _release;
        private readonly Action<string> _warn;
        private bool _held, _failed;

        public MulticastLockHolder(Func<bool> acquire, Action release, Action<string> warn)
        {
            _acquire = acquire; _release = release; _warn = warn;
        }

        public bool IsHeld { get { return _held; } }

        /// <summary>True when the lock is held after the call. A platform that throws or declines is remembered: later calls return false
        /// without asking again, and exactly one warning was issued.</summary>
        public bool Acquire()
        {
            if (_held) return true;
            if (_failed) return false;
            try
            {
                _held = _acquire != null && _acquire();
                if (!_held) Fail("the platform did not grant the lock");
            }
            catch (Exception e)
            {
                _held = false;
                Fail(e.GetType().Name + ": " + FirstLine(e.Message));
            }
            return _held;
        }

        public void Release()
        {
            if (!_held) return;
            _held = false;
            try { if (_release != null) _release(); }
            catch (Exception e) { Say("[PhantomHand] Wi-Fi multicast lock release failed (" + e.GetType().Name + ": " + FirstLine(e.Message) + ")"); }
        }

        private void Fail(string why)
        {
            _failed = true;
            Say("[PhantomHand] Wi-Fi multicast lock not held (" + why + "): UDP broadcast beacons (8791 nodes, 8788 hub) may be dropped on this device");
        }

        private void Say(string message)
        {
            try { if (_warn != null) _warn(message); } catch (Exception) { /* a logger must never break the start */ }
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "no detail";
            int nl = s.IndexOfAny(new[] { '\r', '\n' });
            return nl >= 0 ? s.Substring(0, nl) : s;
        }
    }

    /// <summary>
    /// Quest / Android only: holds a <c>WifiManager.MulticastLock</c> for the life of the app. Without it the Wi-Fi stack can drop the
    /// UDP broadcast beacons the game depends on (node discovery on 8791, hub beacon on 8788); the manifest already declares
    /// CHANGE_WIFI_MULTICAST_STATE (Assets/Plugins/Android/AndroidManifest.xml). Everywhere else (editor, Windows, tests) <see cref="Acquire"/>
    /// is a silent no-op that returns false. Call it once, early, from the main thread (Awake of the bootstrap / scene controller);
    /// calling it again is harmless. Released on <see cref="Application.quitting"/>; if the process is killed instead, Android frees the lock.
    /// </summary>
    public static class AndroidMulticastLock
    {
        public const string Tag = "phantomhand";

#if UNITY_ANDROID && !UNITY_EDITOR
        private static readonly MulticastLockHolder Holder = new MulticastLockHolder(PlatformAcquire, PlatformRelease, Warn);
        private static AndroidJavaObject _lock; // keeps the Java object reachable: MulticastLock.finalize() would release a collected lock
        private static bool _quitHooked;
#endif

        /// <summary>True when the multicast lock is held after the call. Never throws.</summary>
        public static bool Acquire()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            bool held = Holder.Acquire();
            if (held && !_quitHooked) { _quitHooked = true; Application.quitting += OnQuitting; }
            return held;
#else
            return false;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void OnQuitting() { Holder.Release(); }

        private static void Warn(string message) { Debug.LogWarning(message); }

        private static bool PlatformAcquire()
        {
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
            using (var wifi = context.Call<AndroidJavaObject>("getSystemService", "wifi"))
            {
                var l = wifi.Call<AndroidJavaObject>("createMulticastLock", Tag);
                try
                {
                    l.Call("setReferenceCounted", false);
                    l.Call("acquire");
                    if (!l.Call<bool>("isHeld")) { l.Dispose(); return false; }
                }
                catch (Exception) { l.Dispose(); throw; }
                _lock = l;
                Debug.Log("[PhantomHand] Wi-Fi multicast lock acquired (" + Tag + ")");
                return true;
            }
        }

        private static void PlatformRelease()
        {
            var l = _lock; _lock = null;
            if (l == null) return;
            using (l)
            {
                if (l.Call<bool>("isHeld")) l.Call("release");
            }
        }
#endif
    }
}
