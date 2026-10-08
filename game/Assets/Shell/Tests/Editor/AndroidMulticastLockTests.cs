using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Opus.Shell.Tests
{
    /// <summary>
    /// EditMode tests for the Quest Wi-Fi multicast lock. The Android calls themselves only compile on a real player
    /// (#if UNITY_ANDROID &amp;&amp; !UNITY_EDITOR) and need a headset to prove; what is tested here is the rule set around them:
    /// idempotent, one platform call, one warning, nothing escapes as an exception, and a silent no-op in the editor.
    /// </summary>
    public class AndroidMulticastLockTests
    {
        private sealed class FakePlatform
        {
            public int Acquired, Released;
            public bool Grant = true;
            public Exception AcquireThrows, ReleaseThrows;
            public bool Acquire() { Acquired++; if (AcquireThrows != null) throw AcquireThrows; return Grant; }
            public void Release() { Released++; if (ReleaseThrows != null) throw ReleaseThrows; }
        }

        private static MulticastLockHolder Holder(FakePlatform p, List<string> warnings)
        {
            return new MulticastLockHolder(p.Acquire, p.Release, warnings.Add);
        }

        [Test]
        public void Acquire_IsIdempotent_AskingThePlatformOnce()
        {
            var p = new FakePlatform(); var w = new List<string>(); var h = Holder(p, w);
            Assert.IsTrue(h.Acquire()); Assert.IsTrue(h.Acquire()); Assert.IsTrue(h.Acquire());
            Assert.IsTrue(h.IsHeld);
            Assert.AreEqual(1, p.Acquired);
            Assert.AreEqual(0, w.Count);
        }

        [Test]
        public void Release_FreesOnce_AndIsHarmlessAnyTime()
        {
            var p = new FakePlatform(); var w = new List<string>(); var h = Holder(p, w);
            h.Release();                                   // never acquired
            Assert.AreEqual(0, p.Released);
            h.Acquire(); h.Release(); h.Release();
            Assert.IsFalse(h.IsHeld);
            Assert.AreEqual(1, p.Released);
            Assert.AreEqual(0, w.Count);
        }

        [Test]
        public void AcquireAfterRelease_TakesTheLockAgain()
        {
            var p = new FakePlatform(); var w = new List<string>(); var h = Holder(p, w);
            h.Acquire(); h.Release();
            Assert.IsTrue(h.Acquire());
            Assert.AreEqual(2, p.Acquired);
        }

        [Test]
        public void PlatformThrows_ReturnsFalse_WarnsOnce_AndDoesNotRetry()
        {
            var p = new FakePlatform { AcquireThrows = new InvalidOperationException("no WifiManager\nsecond line") };
            var w = new List<string>(); var h = Holder(p, w);
            bool first = true, second = true;
            Assert.DoesNotThrow(() => first = h.Acquire());
            Assert.DoesNotThrow(() => second = h.Acquire());
            Assert.IsFalse(first); Assert.IsFalse(second); Assert.IsFalse(h.IsHeld);
            Assert.AreEqual(1, p.Acquired, "a platform failure is remembered, not retried on every call");
            Assert.AreEqual(1, w.Count);
            StringAssert.Contains("no WifiManager", w[0]);
            StringAssert.DoesNotContain("second line", w[0], "only the first line of the exception goes into the log");
            h.Release();
            Assert.AreEqual(0, p.Released, "nothing held, nothing to release");
        }

        [Test]
        public void PlatformDeclines_ReturnsFalse_WarnsOnce()
        {
            var p = new FakePlatform { Grant = false };
            var w = new List<string>(); var h = Holder(p, w);
            Assert.IsFalse(h.Acquire()); Assert.IsFalse(h.Acquire());
            Assert.AreEqual(1, p.Acquired);
            Assert.AreEqual(1, w.Count);
        }

        [Test]
        public void ReleaseThrows_IsSwallowed_AndTheHolderIsFreeAfterwards()
        {
            var p = new FakePlatform { ReleaseThrows = new InvalidOperationException("binder died") };
            var w = new List<string>(); var h = Holder(p, w);
            h.Acquire();
            Assert.DoesNotThrow(h.Release);
            Assert.IsFalse(h.IsHeld);
            Assert.AreEqual(1, w.Count);
            StringAssert.Contains("binder died", w[0]);
        }

        [Test]
        public void ALoggerThatThrows_CannotBreakTheStart()
        {
            var p = new FakePlatform { AcquireThrows = new InvalidOperationException("x") };
            var h = new MulticastLockHolder(p.Acquire, p.Release, m => { throw new InvalidOperationException("logger down"); });
            Assert.DoesNotThrow(() => h.Acquire());
            Assert.IsFalse(h.IsHeld);
        }

        [Test]
        public void NoDelegates_NoCrash()
        {
            var h = new MulticastLockHolder(null, null, null);
            Assert.IsFalse(h.Acquire());
            Assert.DoesNotThrow(h.Release);
        }

        [Test]
        public void Facade_OutsideAnAndroidPlayer_IsASilentNoOp()
        {
            // EditMode runs with UNITY_EDITOR: the facade compiles to "return false" and touches no JNI.
            Assert.IsFalse(AndroidMulticastLock.Acquire());
            Assert.IsFalse(AndroidMulticastLock.Acquire());
            Assert.IsFalse(string.IsNullOrEmpty(AndroidMulticastLock.Tag));
        }
    }
}
