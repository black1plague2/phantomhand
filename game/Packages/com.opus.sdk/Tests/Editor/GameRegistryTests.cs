using System;
using NUnit.Framework;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    [GameModule("test_dummy_game")]
    public sealed class DummyGameModule : IGameModule
    {
        public GameManifest Manifest => null;
        public void Configure(ParamSet parameters, ISessionContext session) { }
        public void Begin() { }
        public void Pause() { }
        public void Resume() { }
        public void End() { }
        public event Action<TrialEvent> OnTrialEvent;
    }

    public class GameRegistryTests
    {
        [Test]
        public void DiscoversAttributedModulesAutomatically()
        {
            GameRegistry.Rescan();
            Assert.IsTrue(GameRegistry.Games.ContainsKey("test_dummy_game"));
            var instance = GameRegistry.Create("test_dummy_game");
            Assert.IsInstanceOf<DummyGameModule>(instance);
        }

        [Test]
        public void UnknownId_Throws()
        {
            GameRegistry.Rescan();
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => GameRegistry.Create("does_not_exist"));
        }
    }
}
