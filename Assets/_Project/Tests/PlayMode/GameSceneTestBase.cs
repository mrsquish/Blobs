using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Blobs.Tests
{
    /// <summary>Loads the Game scene with simulated input devices, and cleans up in the order the Input System needs.</summary>
    public abstract class GameSceneTestBase : InputTestFixture
    {
        // Destroy the players and the join manager before InputTestFixture resets the input system,
        // otherwise they tear down later against already-reset input state and throw.
        public override void TearDown()
        {
            for (var i = PlayerInput.all.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(PlayerInput.all[i].gameObject);
            if (PlayerInputManager.instance != null)
                Object.DestroyImmediate(PlayerInputManager.instance.gameObject);

            base.TearDown();
        }

        protected static IEnumerator LoadGame()
        {
            SceneManager.LoadScene("Game");
            // Let the camera settle: it is damped, so a broken camera drifts over several frames.
            yield return Frames(60);
        }

        protected IEnumerator JoinGamepads(int count) => JoinGamepads(new Gamepad[count]);

        protected IEnumerator JoinGamepads(Gamepad[] pads)
        {
            for (var i = 0; i < pads.Length; i++)
            {
                pads[i] = InputSystem.AddDevice<Gamepad>();
                PressAndRelease(pads[i].buttonSouth);
                yield return Frames(2);
            }
            yield return Frames(60);
        }

        protected static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
                yield return null;
        }

        protected static Vector2 Flat(Vector3 position) => new(position.x, position.z);
    }
}
