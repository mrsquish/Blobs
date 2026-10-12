using System;
using System.Collections;
using System.Collections.Generic;
using Blobs.Players;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Blobs.Research
{
    /// <summary>
    /// Runs one research minigame instance per researching player, alongside the shared 3D game.
    ///
    /// Each session loads the minigame scene additively with its own 2D physics scene, offsets it far from the
    /// 3D world (and from other instances) so each panel camera only sees its own copy, switches that player's
    /// input to the Research map and lays the panels out with <see cref="ResearchPanelLayout"/>.
    /// </summary>
    public class ResearchSessionManager : MonoBehaviour
    {
        sealed class Session
        {
            public CoopPlayer Player;
            public ResearchTerminal Terminal;
            public Scene Scene;
            public ResearchMinigameRoot Root;
            public bool Ended;
        }

        [SerializeField] CoopSession _coopSession;
        [Tooltip("Scene containing exactly one ResearchMinigameRoot. Must be in the build profile's scene list.")]
        [SerializeField] string _minigameScene = "Research";
        [Tooltip("Distance between minigame instances, and from the 3D world at the origin.")]
        [SerializeField] float _instanceSpacing = 10000f;
        [SerializeField] Color _panelBackground = new(0.05f, 0.06f, 0.1f);
        [SerializeField, Range(0f, 1f)] float _panelTint = 0.15f;

        readonly Dictionary<CoopPlayer, Session> _sessions = new();
        readonly List<int> _visibleSlots = new();

        public event Action<CoopPlayer, ResearchOutcome> ResearchFinished;

        public bool IsResearching(CoopPlayer player) => _sessions.ContainsKey(player);

        void OnEnable()
        {
            if (_coopSession != null)
                _coopSession.PlayerLeft += HandlePlayerLeft;
        }

        void OnDisable()
        {
            if (_coopSession != null)
                _coopSession.PlayerLeft -= HandlePlayerLeft;
        }

        public bool TryBegin(CoopPlayer player, ResearchTerminal terminal)
        {
            if (_sessions.ContainsKey(player))
                return false;

            var session = new Session { Player = player, Terminal = terminal };
            _sessions.Add(player, session);
            player.EnterResearch();
            StartCoroutine(LoadMinigame(session));
            return true;
        }

        public void End(CoopPlayer player, ResearchOutcome outcome)
        {
            if (!_sessions.Remove(player, out var session))
                return;

            session.Ended = true;
            player.ExitResearch();
            session.Terminal.Release(player);
            if (session.Scene.IsValid() && session.Scene.isLoaded)
                SceneManager.UnloadSceneAsync(session.Scene);

            Debug.Log($"Player {player.Index + 1} research {outcome}");
            ResearchFinished?.Invoke(player, outcome);
        }

        IEnumerator LoadMinigame(Session session)
        {
            // The minigame scene is tiny, so a synchronous load (which hands back the Scene, letting us tell
            // instances apart) is fine. Move to Addressables + async loading if it grows.
            var parameters = new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics2D);
            session.Scene = SceneManager.LoadScene(_minigameScene, parameters);
            while (!session.Scene.isLoaded)
                yield return null;

            if (session.Ended)
            {
                SceneManager.UnloadSceneAsync(session.Scene);
                yield break;
            }

            session.Root = FindRoot(session.Scene);
            if (session.Root == null)
            {
                Debug.LogError($"Scene '{_minigameScene}' has no {nameof(ResearchMinigameRoot)} at its root.");
                End(session.Player, ResearchOutcome.Abandoned);
                yield break;
            }

            var player = session.Player;
            session.Root.transform.position = new Vector3((player.Index + 1) * _instanceSpacing, 0f, 0f);
            session.Root.Minigame.Finished += outcome => End(player, outcome);
            session.Root.Minigame.Begin(new ResearchContext(player, session.Scene.GetPhysicsScene2D()));
        }

        // Re-laid out every frame: it is a handful of rects, and joins, leaves and session changes all affect it.
        void LateUpdate()
        {
            _visibleSlots.Clear();
            foreach (var session in _sessions.Values)
            {
                if (session.Root != null)
                    _visibleSlots.Add(session.Player.Index);
            }

            var joined = PlayerInput.all.Count;
            foreach (var session in _sessions.Values)
            {
                if (session.Root == null) continue;
                var viewport = ResearchPanelLayout.Calculate(session.Player.Index, _visibleSlots, joined);
                var background = Color.Lerp(_panelBackground, session.Player.Color, _panelTint);
                session.Root.ShowInPanel(viewport, background);
            }
        }

        void HandlePlayerLeft(CoopPlayer player) => End(player, ResearchOutcome.Abandoned);

        static ResearchMinigameRoot FindRoot(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.TryGetComponent<ResearchMinigameRoot>(out var root))
                    return root;
            }
            return null;
        }
    }
}
