using System.Collections;
using System.Linq;
using Blobs.Players;
using Blobs.Research;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Blobs.Tests
{
    /// <summary>
    /// End-to-end checks on the Game scene with simulated gamepads: what a person sees when they press Play.
    /// </summary>
    public class CoopSmokeTests : GameSceneTestBase
    {
        [UnityTest]
        public IEnumerator BeforeAnyoneJoins_CameraLooksDownAtTheGround()
        {
            yield return LoadGame();

            var camera = Camera.main;
            Assert.That(camera.transform.position.y, Is.GreaterThan(3f), "camera has sunk to (or below) the ground");
            Assert.That(Physics.Raycast(camera.transform.position, camera.transform.forward, 200f), Is.True, "camera is not looking at the level");
            Assert.That(camera.fieldOfView, Is.GreaterThanOrEqualTo(20f), "camera has zoomed in to a sliver");
            AssertSpawnAreaInShot(camera);
        }

        static void AssertSpawnAreaInShot(Camera camera)
        {
            foreach (var spawn in GameObject.Find("Coop Session").GetComponentsInChildren<Transform>())
            {
                if (!spawn.name.StartsWith("Spawn")) continue;
                var viewport = camera.WorldToViewportPoint(spawn.position);
                Assert.That(viewport.z, Is.GreaterThan(0f), $"{spawn.name} is behind the camera");
                Assert.That(viewport.x, Is.InRange(0.05f, 0.95f), $"{spawn.name} is off screen horizontally");
                Assert.That(viewport.y, Is.InRange(0.05f, 0.95f), $"{spawn.name} is off screen vertically");
            }
        }

        [UnityTest]
        public IEnumerator TwoGamepads_JoinWithDistinctSlotsAndAreBothInShot()
        {
            yield return LoadGame();
            yield return JoinGamepads(2);

            Assert.That(PlayerInput.all.Select(p => p.playerIndex), Is.EquivalentTo(new[] { 0, 1 }));

            var camera = Camera.main;
            Assert.That(camera.transform.position.y, Is.GreaterThan(3f));
            Assert.That(camera.fieldOfView, Is.GreaterThanOrEqualTo(20f));
            foreach (var player in PlayerInput.all)
            {
                var viewport = camera.WorldToViewportPoint(player.transform.position);
                Assert.That(viewport.z, Is.GreaterThan(0f), $"{player.name} is behind the camera");
                Assert.That(viewport.x, Is.InRange(0f, 1f), $"{player.name} is off screen horizontally");
                Assert.That(viewport.y, Is.InRange(0f, 1f), $"{player.name} is off screen vertically");
            }
        }

        [UnityTest]
        public IEnumerator SharedKeyboard_TwoPlayersJoinAndMoveIndependently()
        {
            yield return LoadGame();
            var keyboard = InputSystem.AddDevice<Keyboard>();

            PressAndRelease(keyboard.dKey);
            yield return Frames(2);
            PressAndRelease(keyboard.rightArrowKey);
            yield return new WaitForSeconds(0.5f);

            var left = PlayerInput.all.Single(p => p.currentControlScheme == "KeyboardLeft");
            var right = PlayerInput.all.Single(p => p.currentControlScheme == "KeyboardRight");
            Assert.That(left.playerIndex, Is.EqualTo(0));
            Assert.That(right.playerIndex, Is.EqualTo(1));

            // Pressing the same side again must not add a third player.
            PressAndRelease(keyboard.wKey);
            yield return Frames(2);
            Assert.That(PlayerInput.all.Count, Is.EqualTo(2));

            var leftStart = Flat(left.transform.position);
            var rightStart = Flat(right.transform.position);
            Press(keyboard.upArrowKey);
            yield return new WaitForSeconds(0.5f);
            Release(keyboard.upArrowKey);
            yield return null;

            Assert.That(Vector2.Distance(Flat(right.transform.position), rightStart), Is.GreaterThan(1f), "arrow keys did not move the right-hand player");
            Assert.That(Vector2.Distance(Flat(left.transform.position), leftStart), Is.LessThan(0.05f), "arrow keys also moved the WASD player");

            leftStart = Flat(left.transform.position);
            rightStart = Flat(right.transform.position);
            Press(keyboard.aKey);
            yield return new WaitForSeconds(0.5f);
            Release(keyboard.aKey);
            yield return null;

            Assert.That(Vector2.Distance(Flat(left.transform.position), leftStart), Is.GreaterThan(1f), "WASD did not move the left-hand player");
            Assert.That(Vector2.Distance(Flat(right.transform.position), rightStart), Is.LessThan(0.05f), "WASD also moved the arrow-key player");
        }

        [UnityTest]
        public IEnumerator Researching_OpensACornerPanelForThatPlayerOnly_AndExitClosesIt()
        {
            yield return LoadGame();
            var pads = new Gamepad[2];
            yield return JoinGamepads(pads);

            var researcher = PlayerInput.all.Single(p => p.playerIndex == 0);
            var explorer = PlayerInput.all.Single(p => p.playerIndex == 1);
            var terminal = Object.FindAnyObjectByType<ResearchTerminal>();
            researcher.GetComponent<PlayerMotor>().Teleport(
                terminal.transform.position + terminal.transform.forward * 1.2f, Quaternion.identity);
            yield return Frames(5);

            PressAndRelease(pads[0].buttonWest);
            yield return Frames(10);

            Assert.That(researcher.currentActionMap.name, Is.EqualTo("Research"));
            Assert.That(explorer.currentActionMap.name, Is.EqualTo("Player"));
            var minigame = Object.FindAnyObjectByType<ResearchMinigameRoot>();
            Assert.That(minigame, Is.Not.Null, "research scene did not load");
            Assert.That(minigame.Camera.enabled, Is.True);
            Assert.That(minigame.Camera.rect, Is.EqualTo(ResearchPanelLayout.CornerPanel(0)));

            PressAndRelease(pads[0].buttonEast);
            yield return Frames(10);

            Assert.That(researcher.currentActionMap.name, Is.EqualTo("Player"));
            Assert.That(Object.FindAnyObjectByType<ResearchMinigameRoot>(), Is.Null, "research scene did not unload");
        }
    }
}
