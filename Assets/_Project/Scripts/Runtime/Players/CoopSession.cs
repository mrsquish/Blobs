using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blobs.Players
{
    /// <summary>
    /// Owns the local co-op roster. PlayerInputManager spawns a player prefab when an unpaired device presses
    /// a button (see LocalJoinController); this places, colours and registers that player. The camera
    /// (SplitScreenDirector) follows whoever is in <see cref="Players"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerInputManager))]
    public class CoopSession : MonoBehaviour
    {
        [SerializeField] Transform[] _spawnPoints = Array.Empty<Transform>();
        [SerializeField] Color[] _playerColors =
        {
            new(0.98f, 0.36f, 0.36f), // P1 red
            new(0.33f, 0.62f, 1.00f), // P2 blue
            new(1.00f, 0.82f, 0.25f), // P3 yellow
            new(0.45f, 0.85f, 0.40f), // P4 green
        };

        readonly List<CoopPlayer> _players = new();
        PlayerInputManager _manager;

        public event Action<CoopPlayer> PlayerJoined;
        public event Action<CoopPlayer> PlayerLeft;

        public IReadOnlyList<CoopPlayer> Players => _players;

        void Awake() => _manager = GetComponent<PlayerInputManager>();

        void OnEnable()
        {
            _manager.onPlayerJoined += HandlePlayerJoined;
            _manager.onPlayerLeft += HandlePlayerLeft;
        }

        void OnDisable()
        {
            _manager.onPlayerJoined -= HandlePlayerJoined;
            _manager.onPlayerLeft -= HandlePlayerLeft;
        }

        void HandlePlayerJoined(PlayerInput input)
        {
            var player = input.GetComponent<CoopPlayer>();
            var slot = input.playerIndex;

            if (_playerColors.Length > 0)
                player.SetColor(_playerColors[slot % _playerColors.Length]);
            if (_spawnPoints.Length > 0)
            {
                var spawn = _spawnPoints[slot % _spawnPoints.Length];
                input.GetComponent<PlayerMotor>().Teleport(spawn.position, spawn.rotation);
            }

            input.gameObject.name = $"Player {slot + 1}";
            _players.Add(player);

            Debug.Log($"Player {slot + 1} joined ({input.currentControlScheme}: {string.Join(", ", input.devices)})");
            PlayerJoined?.Invoke(player);
        }

        void HandlePlayerLeft(PlayerInput input)
        {
            var player = input.GetComponent<CoopPlayer>();
            _players.Remove(player);

            Debug.Log($"Player {input.playerIndex + 1} left");
            PlayerLeft?.Invoke(player);
        }
    }
}
