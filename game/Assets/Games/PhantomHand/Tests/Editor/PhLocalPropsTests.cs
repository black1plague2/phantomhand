using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Opus.Games.PhantomHand.EditorTools;
using Opus.Games.PhantomHand.Presentation;
using UnityEngine;
using B = Opus.Shell.Editor.PhantomHandSceneBuilder;
using Object = UnityEngine.Object;

namespace Opus.Games.PhantomHand.Tests
{
    /// <summary>
    /// The Asset Store props (PhLocalProps, PhModels.LoadLocal, the slots the scene builder commits). The packs exist only on the build PC, so most of what matters is that a clone WITHOUT them is unchanged:
    /// nothing spawned, nothing switched off, nothing committed that points at a pack. The decisions and the layout arithmetic are pure and also run outside Unity; the tests with GameObjects and Resources
    /// are marked UnityEngine and need the engine (run them in the editor).
    /// </summary>
    public class PhLocalPropsTests
    {
        private readonly List<Object> _made = new List<Object>();
        private bool _useLocal, _useModels;

        [SetUp]
        public void Remember() { _useLocal = PhModels.UseLocal; _useModels = PhModels.UseModels; }

        [TearDown]
        public void Cleanup()
        {
            PhModels.UseLocal = _useLocal; PhModels.UseModels = _useModels;
            foreach (var o in _made) if (o != null) Object.DestroyImmediate(o);
            _made.Clear();
        }

        // ---- names and folders (pure) ----------------------------------------------------------------------------------------------

        [Test]
        public void TheLocalNames_AreNotInAll_SoNoCommittedWrapperIsExpectedForThem()
        {
            Assert.AreEqual("PhantomModelsLocal/", PhModels.LocalFolder); Assert.AreEqual("PH_Books", PhModels.Books); Assert.AreEqual("PH_Sideboard", PhModels.Sideboard);
            CollectionAssert.DoesNotContain(PhModels.All, PhModels.Books); CollectionAssert.DoesNotContain(PhModels.All, PhModels.Sideboard);
            Assert.AreEqual(12, PhModels.All.Length, "the committed wrappers are still the 12");
            Assert.AreNotEqual(PhModels.ResourceFolder, PhModels.LocalFolder, "two different Resources paths: a local wrapper never shadows a committed asset path");
        }

        [Test]
        public void TheBake_WritesWhereTheGameLooks_InsideTheFolderThatIsGitIgnored()
        {
            Assert.AreEqual("Assets/Art/PhantomHand/Models/Local", PhantomModelImporter.LocalRoot);
            StringAssert.StartsWith(PhantomModelImporter.LocalRoot + "/", PhantomModelImporter.LocalResourcesDir);
            Assert.IsTrue(PhantomModelImporter.LocalResourcesDir.EndsWith("/Resources/" + PhModels.LocalFolder.TrimEnd('/')), PhantomModelImporter.LocalResourcesDir + " must be a Resources folder holding " + PhModels.LocalFolder);
        }

        // ---- which slots will spawn (pure) -----------------------------------------------------------------------------------------

        private static readonly PhLocalProps.Slot[] TwoSlots =
        {
            new PhLocalProps.Slot(PhModels.Books, new Vector3(-0.4f, 0.75f, 0.62f), 0f), new PhLocalProps.Slot(PhModels.Sideboard, new Vector3(2.45f, 0f, 0.9f), 270f),
        };

        [Test]
        public void Plan_WithNoLocalWrapper_IsEmpty_SoNothingIsCreatedAndNothingChanged()
        {
            Assert.IsEmpty(PhLocalProps.Plan(TwoSlots, n => false));
            Assert.IsEmpty(PhLocalProps.Plan(new PhLocalProps.Slot[0], n => true));
            Assert.IsEmpty(PhLocalProps.Plan(null, n => true));
            Assert.IsEmpty(PhLocalProps.Plan(TwoSlots, null));
        }

        [Test]
        public void Plan_KeepsExactlyTheSlotsWhoseWrapperExists_InOrder()
        {
            var onlyBooks = PhLocalProps.Plan(TwoSlots, n => n == PhModels.Books);
            Assert.AreEqual(1, onlyBooks.Count); Assert.AreEqual(PhModels.Books, onlyBooks[0].wrapper); Assert.AreEqual(0.62f, onlyBooks[0].position.z);
            var both = PhLocalProps.Plan(TwoSlots, n => true);
            Assert.AreEqual(new[] { PhModels.Books, PhModels.Sideboard }, both.Select(s => s.wrapper).ToArray());
            Assert.AreEqual(270f, both[1].yaw);
        }

        [Test]
        public void Plan_SkipsASlotWithoutAName_EvenWhenEverythingExists()
        {
            var slots = new[] { new PhLocalProps.Slot("", Vector3.zero, 0f), new PhLocalProps.Slot(null, Vector3.zero, 0f), new PhLocalProps.Slot(PhModels.Books, Vector3.zero, 0f) };
            Assert.AreEqual(1, PhLocalProps.Plan(slots, n => true).Count);
        }

        [Test]
        public void PhLocalProps_SerializesNoObjectReference_OnlyNamesPositionsAndYaws()
        {
            // the committed scene may hold this component only because it cannot hold a reference to a pack asset or a local wrapper
            foreach (var f in typeof(PhLocalProps).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                bool serialized = f.IsPublic || f.GetCustomAttributes(typeof(SerializeField), true).Length > 0;
                if (!serialized) continue;
                Type t = f.FieldType.IsArray ? f.FieldType.GetElementType() : f.FieldType;
                Assert.AreEqual(typeof(PhLocalProps.Slot), t, "serialized field " + f.Name);
            }
            var allowed = new HashSet<Type> { typeof(string), typeof(float), typeof(Vector3) };
            foreach (var f in typeof(PhLocalProps.Slot).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Assert.IsTrue(allowed.Contains(f.FieldType), "Slot." + f.Name + " is a " + f.FieldType.Name + ": only a name, a position and a yaw may be saved in the scene");
        }

        // ---- the slots the scene builder commits (pure, from the builder's own constants) ----------------------------------------

        private static float[] Foot(float cx, float cz, float half) { return new[] { cx - half, cx + half, cz - half, cz + half }; }                       // x0, x1, z0, z1

        /// <summary>Plan-view distance between two rectangles (x0, x1, z0, z1); 0 when they touch or overlap.</summary>
        private static float Gap(float[] a, float[] b)
        {
            float dx = Mathf.Max(Mathf.Max(b[0] - a[1], a[0] - b[1]), 0f), dz = Mathf.Max(Mathf.Max(b[2] - a[3], a[2] - b[3]), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private const float ArmHalfWidth = 0.044f;      // half width of the arm-rest outline (BuildOutline), the same box the bowl and cup were cleared against
        private static float[] RealArm { get { return new[] { B.ArmX - ArmHalfWidth, B.ArmX + ArmHalfWidth, B.ElbowZ, B.ElbowZ + B.ForearmLen + B.HandLen }; } }
        private static float[] VirtualArm { get { return new[] { B.ArmX - B.OffsetM - ArmHalfWidth, B.ArmX - B.OffsetM + ArmHalfWidth, B.ElbowZ, B.ElbowZ + B.ForearmLen + B.HandLen }; } }
        private static float[] Ruler { get { return new[] { -PhantomAnchors.RulerLengthM / 2f, PhantomAnchors.RulerLengthM / 2f, PhantomAnchors.RulerDistanceM - 0.025f, PhantomAnchors.RulerDistanceM + 0.025f }; } }
        private static float[] TableTop { get { return new[] { -0.6f, 0.6f, B.TableCenterZ - 0.35f, B.TableCenterZ + 0.35f }; } }
        private const float RoomX = 2.45f, RoomZMin = -1.55f, RoomZMax = 2.65f;     // inner faces of the walls built in PhantomHandSceneBuilder (Wall_Left / Right at x +-2.5, Back at z -1.6, Front at z 2.7, each 10 cm thick)

        private static PhLocalProps.Slot Slot(string wrapper) { return B.LocalSlots().Single(s => s.wrapper == wrapper); }

        [Test]
        public void TheGapArithmetic_MeasuresTheDistanceBetweenRectangles()
        {
            Assert.AreEqual(2f, Gap(new[] { 0f, 1f, 0f, 1f }, new[] { 3f, 4f, 0f, 1f }), 1e-6f);
            Assert.AreEqual(5f, Gap(new[] { 0f, 1f, 0f, 1f }, new[] { 4f, 5f, 5f, 6f }), 1e-6f, "diagonal: 3 and 4");
            Assert.AreEqual(0f, Gap(new[] { 0f, 2f, 0f, 2f }, new[] { 1f, 3f, 1f, 3f }), "overlap");
            Assert.AreEqual(0f, Gap(Foot(B.ArmX - B.OffsetM, B.TableCenterZ, 0.15f), VirtualArm), "a stack standing on the virtual arm is inside its zone");
        }

        [Test]
        public void TheBooks_SitOnTheTablesFarLeftCorner_ClearOfTheArmsTheRulerAndTheEdges()
        {
            var s = Slot(PhModels.Books);
            float half = PhantomModelImporter.LocalBookDesignM / 2f;
            var foot = Foot(s.position.x, s.position.z, half);
            Assert.AreEqual(B.TableTopY, s.position.y, 1e-6f, "bottom-centre pivot on the table top");
            Assert.Less(s.position.x, 0f, "left of the seat's centre line"); Assert.Greater(s.position.z, B.TableCenterZ, "on the far half of the table");
            var table = TableTop;
            float edge = Mathf.Min(Mathf.Min(foot[0] - table[0], table[1] - foot[1]), Mathf.Min(foot[2] - table[2], table[3] - foot[3]));
            Assert.GreaterOrEqual(edge, 0.04f, "a stack of the design footprint stays on the table, 4 cm or more from every edge (5.0 cm at the chosen spot)");
            Assert.GreaterOrEqual(Gap(foot, RealArm), 0.30f, "real-arm area (38.6 cm)");
            Assert.GreaterOrEqual(Gap(foot, VirtualArm), 0.20f, "virtual arm (23.6 cm)");
            Assert.GreaterOrEqual(Gap(foot, Ruler), 0.09f, "probe ruler (9.5 cm)");
        }

        [Test]
        public void TheSideboard_StandsOnTheFloor_BackOnTheRightWall_FacingTheRoom()
        {
            var s = Slot(PhModels.Sideboard);
            Assert.AreEqual(RoomX, B.WallRightInnerX, "the inner face of Wall_Right");
            Assert.AreEqual(B.WallRightInnerX, s.position.x, 1e-6f, "the pivot is the middle of the back face: on the wall");
            Assert.AreEqual(0f, s.position.y, "bottom at the floor");
            Assert.AreEqual(0.9f, s.position.z, 0.05f, "z about 0.9");
            // a wrapper's front is +z; Quaternion.Euler(0, yaw, 0) turns it to (sin yaw, cos yaw)
            float fx = Mathf.Sin(s.yaw * Mathf.Deg2Rad), fz = Mathf.Cos(s.yaw * Mathf.Deg2Rad);
            Assert.AreEqual(-1f, fx, 1e-5f, "the front points to -x, away from the wall"); Assert.AreEqual(0f, fz, 1e-5f);
        }

        [Test]
        public void TheSideboard_FitsTheRoom_AtTheLargestSizeTheBakeAccepts_AndStaysClearOfTheTable()
        {
            var s = Slot(PhModels.Sideboard);
            var r = PhLocalFit.Sideboard;
            var biggest = new[] { s.position.x - r.DMax, s.position.x, s.position.z - r.WMax / 2f, s.position.z + r.WMax / 2f };      // depth into the room, width along the wall
            Assert.GreaterOrEqual(biggest[0], -RoomX); Assert.LessOrEqual(biggest[1], RoomX + 1e-5f);
            Assert.GreaterOrEqual(biggest[2], RoomZMin); Assert.LessOrEqual(biggest[3], RoomZMax);
            Assert.GreaterOrEqual(Gap(biggest, TableTop), 0.5f, "the widest and deepest sideboard still leaves half a metre to the table");
            Assert.GreaterOrEqual(Gap(biggest, RealArm), 0.5f); Assert.GreaterOrEqual(Gap(biggest, Ruler), 0.5f);
        }

        [Test]
        public void TheSlots_AreExactlyTheTwoProps_AndCarryOnlyNames()
        {
            var slots = B.LocalSlots();
            Assert.AreEqual(new[] { PhModels.Books, PhModels.Sideboard }, slots.Select(s => s.wrapper).ToArray());
        }

        // ---- nothing committed may depend on a pack (pure file scan) -----------------------------------------------------------

        private static readonly string[] IgnoredFolders = { "Assets/Free", "Assets/Books", "Assets/Dark Wave Paint", "Assets/Furniture_ges1", "Assets/Art/PhantomHand/Models/Local" };

        [Test]
        public void GitIgnore_ListsTheFourPacksTheirMetas_AndTheLocalWrapperFolder()
        {
            string file = ".gitignore";                                         // the editor's working directory is the project folder (game/)
            if (!File.Exists(file)) { Assert.Inconclusive("no " + file + " in " + Directory.GetCurrentDirectory()); return; }
            var lines = File.ReadAllLines(file).Select(l => l.Trim()).ToList();
            Func<string, bool> has = path => lines.Contains("/" + path) || lines.Contains("/[Aa]" + path.Substring(1));
            foreach (var folder in IgnoredFolders)
            {
                Assert.IsTrue(has(folder + "/"), folder + "/ must be ignored");
                Assert.IsTrue(has(folder + ".meta"), folder + ".meta must be ignored");
            }
        }

        [Test]
        public void NoCommittedAsset_ReferencesAPackOrALocalWrapper()
        {
            // every asset that is not inside an ignored folder: no guid of anything inside one (a clone without the packs has none, and then there is nothing to find)
            var banned = new Dictionary<string, string>();
            foreach (var dir in IgnoredFolders)
                if (Directory.Exists(dir))
                    foreach (var meta in Directory.GetFiles(dir, "*.meta", SearchOption.AllDirectories))
                        foreach (var line in File.ReadLines(meta))
                            if (line.StartsWith("guid: ")) { banned[line.Substring(6).Trim()] = meta; break; }
            if (banned.Count == 0 || !Directory.Exists("Assets")) return;
            var rx = new Regex("guid: ([0-9a-f]{32})");
            var kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".unity", ".prefab", ".mat", ".asset", ".controller", ".anim", ".overrideController", ".lighting", ".mixer", ".playable" };
            var bad = new List<string>();
            foreach (var file in Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories))
            {
                if (!kinds.Contains(Path.GetExtension(file))) continue;
                string norm = file.Replace('\\', '/');
                if (IgnoredFolders.Any(d => norm.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase))) continue;
                foreach (Match m in rx.Matches(File.ReadAllText(file)))
                    if (banned.ContainsKey(m.Groups[1].Value)) { bad.Add(norm + " -> " + banned[m.Groups[1].Value]); break; }
            }
            Assert.IsEmpty(bad, "committed assets that point into a git-ignored pack folder:\n" + string.Join("\n", bad.ToArray()));
        }

        // ---- with the engine ---------------------------------------------------------------------------------------------------

        private GameObject Make(string name, Transform parent = null)
        {
            var go = new GameObject(name); _made.Add(go);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>A stand-in for a wrapper prefab: an object with a child, parked at the origin outside the scene under test.</summary>
        private GameObject Stand(string name)
        {
            var go = Make(name); Make("Model", go.transform);
            return go;
        }

        [Test, Category("UnityEngine")]
        public void Load_FallsBackToTheCommittedWrapper_WhenThereIsNoLocalOne()
        {
            Assert.IsNull(PhModels.LoadLocal(PhModels.Hand), "no pack ever gives a hand");
            var committed = Resources.Load<GameObject>(PhModels.ResourceFolder + PhModels.Hand);
            Assert.IsNotNull(committed, "the baked wrappers are committed");
            Assert.AreSame(committed, PhModels.Load(PhModels.Hand));
            foreach (var n in PhModels.All.Concat(new[] { PhModels.Books, PhModels.Sideboard }))        // a machine that has baked the packs finds a local one for some names; the rule holds either way
            {
                var local = Resources.Load<GameObject>(PhModels.LocalFolder + n);
                var expected = local != null ? local : Resources.Load<GameObject>(PhModels.ResourceFolder + n);
                Assert.AreSame(expected, PhModels.Load(n), n);
            }
        }

        [Test, Category("UnityEngine")]
        public void Load_GivesTheCommittedWrapper_WhenTheLocalOnesAreSwitchedOff_AndNothingWhenModelsAre()
        {
            PhModels.UseLocal = false;
            foreach (var n in PhModels.All) Assert.AreSame(Resources.Load<GameObject>(PhModels.ResourceFolder + n), PhModels.Load(n), n);
            Assert.IsNull(PhModels.LoadLocal(PhModels.Stone));
            Assert.IsNull(PhModels.Load(PhModels.Books), "no committed wrapper for the new names");
            PhModels.UseLocal = true; PhModels.UseModels = false;
            Assert.IsNull(PhModels.Load(PhModels.Hand)); Assert.IsNull(PhModels.LoadLocal(PhModels.Stone));
        }

        [Test, Category("UnityEngine")]
        public void Spawn_WithNoLocalWrapper_CreatesNothing_AndChangesNothing()
        {
            var env = Make("Environment"); var props = env.AddComponent<PhLocalProps>(); props.slots = B.LocalSlots();
            var table = Make("Table", env.transform); var committed = Make(PhModels.Table, table.transform);
            int before = env.GetComponentsInChildren<Transform>(true).Length;
            Assert.AreEqual(0, props.Spawn(n => null));
            Assert.AreEqual(before, env.GetComponentsInChildren<Transform>(true).Length, "nothing created");
            Assert.IsTrue(committed.activeSelf, "the scene's table is untouched");
            PhModels.UseLocal = false;                                    // the real lookup, with the local folder switched off (a machine with baked packs would find wrappers)
            Assert.AreEqual(0, props.Spawn());
            Assert.AreEqual(before, env.GetComponentsInChildren<Transform>(true).Length); Assert.IsTrue(committed.activeSelf);
            props.Clear();
            Assert.AreEqual(before, env.GetComponentsInChildren<Transform>(true).Length); Assert.IsTrue(committed.activeSelf);
        }

        [Test, Category("UnityEngine")]
        public void Spawn_PutsTheWrappersUnderItself_NeverSaved_AndClearTakesThemAway()
        {
            var env = Make("Environment"); var props = env.AddComponent<PhLocalProps>(); props.slots = B.LocalSlots();
            var books = Stand(PhModels.Books); var side = Stand(PhModels.Sideboard);
            Func<string, GameObject> load = n => n == PhModels.Books ? books : n == PhModels.Sideboard ? side : null;
            Assert.AreEqual(2, props.Spawn(load));
            var b = env.transform.Find(PhModels.Books); var s = env.transform.Find(PhModels.Sideboard);
            Assert.IsNotNull(b); Assert.IsNotNull(s);
            Assert.AreEqual(-0.40f, b.position.x, 1e-5f); Assert.AreEqual(B.TableTopY, b.position.y, 1e-5f); Assert.AreEqual(0.62f, b.position.z, 1e-5f);
            Assert.AreEqual(270f, s.eulerAngles.y, 1e-3f); Assert.AreEqual(B.WallRightInnerX, s.position.x, 1e-5f);
            foreach (var t in env.GetComponentsInChildren<Transform>(true).Where(t => t != env.transform))
                Assert.AreEqual(HideFlags.DontSave, t.gameObject.hideFlags, t.name + ": nothing it creates may be saved into the scene");
            Assert.AreEqual(2, props.Spawn(load), "spawning again replaces, it does not add");
            Assert.AreEqual(1, env.transform.Cast<Transform>().Count(t => t.name == PhModels.Books));
            props.Clear();
            Assert.AreEqual(0, env.transform.childCount);
        }

        [Test, Category("UnityEngine")]
        public void Spawn_PutsALocalTableAtThePoseOfTheScenesTable_SwitchesThatOneOff_AndClearSwitchesItBackOn()
        {
            var env = Make("Environment"); var props = env.AddComponent<PhLocalProps>();
            var table = Make("Table", env.transform); var committed = Make(PhModels.Table, table.transform);
            committed.transform.SetPositionAndRotation(new Vector3(0f, 0.75f, 0.47f), Quaternion.Euler(0f, 10f, 0f));
            var local = Stand(PhModels.Table);
            Assert.AreEqual(1, props.Spawn(n => n == PhModels.Table ? local : null));
            Assert.IsFalse(committed.activeSelf, "the scene's own table is switched off");
            var swapped = table.transform.Cast<Transform>().Single(t => t.gameObject != committed);
            Assert.AreEqual(PhModels.Table, swapped.name); Assert.IsTrue(swapped.gameObject.activeSelf);
            Assert.AreEqual(0f, Vector3.Distance(committed.transform.position, swapped.position), 1e-5f, "same place"); Assert.AreEqual(10f, swapped.eulerAngles.y, 1e-3f, "same turn");
            Assert.AreEqual(HideFlags.DontSave, swapped.gameObject.hideFlags);
            props.Clear();
            Assert.IsTrue(committed.activeSelf, "switched back on"); Assert.AreEqual(1, table.transform.childCount);
        }

        [Test, Category("UnityEngine")]
        public void Spawn_LeavesTheBoxTableAlone_WhichHasNoModelToReplace()
        {
            var env = Make("Environment"); var props = env.AddComponent<PhLocalProps>();
            var table = Make("Table", env.transform); Make("TableTopSurface", table.transform);
            var local = Stand(PhModels.Table);
            Assert.AreEqual(0, props.Spawn(n => n == PhModels.Table ? local : null));
            Assert.AreEqual(1, table.transform.childCount);
        }
    }
}
