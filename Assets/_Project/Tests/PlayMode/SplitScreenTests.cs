using System;
using System.Collections;
using System.IO;
using System.Linq;
using Blobs.Cameras;
using Blobs.Players;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Blobs.Tests
{
    /// <summary>
    /// Dynamic split-screen behaviour in the real Game scene. Set the BLOBS_CAPTURE_DIR environment variable to also
    /// save the composited frame at each step as a PNG (needs a graphics device, i.e. not -nographics).
    /// </summary>
    public class SplitScreenTests : GameSceneTestBase
    {
        const float SettleSeconds = 1.5f;

        [UnityTest]
        public IEnumerator TwoPlayersFarApart_SplitIntoTwoViews_AndMergeWhenTheyRegroup()
        {
            yield return LoadGame();
            yield return JoinGamepads(2);
            var director = Object.FindAnyObjectByType<SplitScreenDirector>();
            var (left, right) = (Player(0), Player(1));

            Assert.That(director.IsSplit, Is.False, "players start together");
            Capture("1-together");

            Place(left, new Vector3(-55f, 0f, 0f));
            Place(right, new Vector3(55f, 0f, 0f));
            yield return new WaitForSeconds(SettleSeconds);

            Assert.That(director.DistinctViewCount, Is.EqualTo(2));
            Assert.That(director.RenderedScreenFraction, Is.LessThan(1.1f), "each view should render only its own half");
            var leftOnScreen = OnScreen(director, left);
            var rightOnScreen = OnScreen(director, right);
            Assert.That(leftOnScreen.x, Is.InRange(0.1f, 0.45f), "left player should sit in the left half of the screen");
            Assert.That(rightOnScreen.x, Is.InRange(0.55f, 0.9f), "right player should sit in the right half of the screen");
            Assert.That(leftOnScreen.y, Is.InRange(0.2f, 0.8f));
            Assert.That(rightOnScreen.y, Is.InRange(0.2f, 0.8f));
            Capture("2-split-two");

            Place(left, new Vector3(-2f, 0f, 0f));
            Place(right, new Vector3(2f, 0f, 0f));
            yield return new WaitForSeconds(SettleSeconds);

            Assert.That(director.IsSplit, Is.False, "regrouped players should be back on one view");
            Assert.That(director.RenderedScreenFraction, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(director.CameraShowing(left).rect, Is.EqualTo(new Rect(0f, 0f, 1f, 1f)));
        }

        [UnityTest]
        public IEnumerator PlayersCloseTogether_ShareAView_WhileAFarPlayerGetsTheirOwn()
        {
            yield return LoadGame();
            yield return JoinGamepads(3);
            var director = Object.FindAnyObjectByType<SplitScreenDirector>();

            Place(Player(0), new Vector3(-55f, 0f, -3f));
            Place(Player(1), new Vector3(-52f, 0f, 3f));
            Place(Player(2), new Vector3(55f, 0f, 0f));
            yield return new WaitForSeconds(SettleSeconds);

            Assert.That(director.DistinctViewCount, Is.EqualTo(2));
            Assert.That(director.CameraShowing(Player(0)), Is.SameAs(director.CameraShowing(Player(1))));
            Assert.That(director.CameraShowing(Player(2)), Is.Not.SameAs(director.CameraShowing(Player(0))));
            for (var slot = 0; slot < 3; slot++)
                OnScreen(director, Player(slot));
            Capture("3-pair-and-one");
        }

        [UnityTest]
        public IEnumerator FourPlayersInFourCorners_GetFourViews()
        {
            yield return LoadGame();
            yield return JoinGamepads(4);
            var director = Object.FindAnyObjectByType<SplitScreenDirector>();

            Place(Player(0), new Vector3(-55f, 0f, 45f));
            Place(Player(1), new Vector3(55f, 0f, 45f));
            Place(Player(2), new Vector3(-55f, 0f, -45f));
            Place(Player(3), new Vector3(55f, 0f, -45f));
            yield return new WaitForSeconds(SettleSeconds);

            Assert.That(director.DistinctViewCount, Is.EqualTo(4));
            Assert.That(director.RenderedScreenFraction, Is.LessThan(1.2f), "each view should render only its own section");
            var topLeft = OnScreen(director, Player(0));
            var bottomRight = OnScreen(director, Player(3));
            Assert.That(topLeft.x, Is.LessThan(0.5f));
            Assert.That(topLeft.y, Is.GreaterThan(0.5f));
            Assert.That(bottomRight.x, Is.GreaterThan(0.5f));
            Assert.That(bottomRight.y, Is.LessThan(0.5f));
            Capture("4-split-four");
        }

        static CoopPlayer Player(int slot) => PlayerInput.all.Single(p => p.playerIndex == slot).GetComponent<CoopPlayer>();

        static void Place(CoopPlayer player, Vector3 position) =>
            player.GetComponent<PlayerMotor>().Teleport(position + Vector3.up * 0.05f, Quaternion.identity);

        /// <summary>
        /// Where the player appears on the whole screen, via the real camera that shows them (which renders only its
        /// section, so its viewport coordinates are mapped back through Camera.rect). Fails if they are not visible.
        /// </summary>
        static Vector2 OnScreen(SplitScreenDirector director, CoopPlayer player)
        {
            var viewCamera = director.CameraShowing(player);
            Assert.That(viewCamera.enabled, Is.True, $"{player.name}'s view is not rendering");
            var inSection = viewCamera.WorldToViewportPoint(player.transform.position + Vector3.up);
            Assert.That(inSection.z, Is.GreaterThan(0f), $"{player.name} is behind their camera");
            Assert.That(inSection.x, Is.InRange(0f, 1f), $"{player.name} is outside the section their camera renders");
            Assert.That(inSection.y, Is.InRange(0f, 1f), $"{player.name} is outside the section their camera renders");

            var section = viewCamera.rect;
            var viewport = section.min + Vector2.Scale(inSection, section.size);
            Assert.That(viewport.x, Is.InRange(0.05f, 0.95f), $"{player.name} is off screen horizontally");
            Assert.That(viewport.y, Is.InRange(0.05f, 0.95f), $"{player.name} is off screen vertically");
            return viewport;
        }

        static void Capture(string name)
        {
            var directory = Environment.GetEnvironmentVariable("BLOBS_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) return;

            const int width = 1280;
            var height = Mathf.RoundToInt(width * Screen.height / (float)Mathf.Max(1, Screen.width));
            var target = RenderTexture.GetTemporary(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                Object.FindAnyObjectByType<SplitScreenDirector>().RenderTo(target);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(image);
            }
        }
    }
}
