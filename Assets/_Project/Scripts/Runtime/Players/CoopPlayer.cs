using System;
using Blobs.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blobs.Players
{
    /// <summary>
    /// Identity and state for one local player: slot index, colour, connection and what mode they are in.
    /// Lives on the player prefab spawned by <see cref="PlayerInputManager"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerInput))]
    public class CoopPlayer : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] Renderer[] _tintedRenderers = Array.Empty<Renderer>();

        PlayerInput _input;
        MaterialPropertyBlock _block;

        public event Action<CoopPlayer> ConnectionChanged;

        // Initialised lazily rather than in Awake: PlayerInputManager raises onPlayerJoined from inside
        // PlayerInput.OnEnable, while the prefab is still being instantiated and before our Awake has run.
        public PlayerInput Input => _input != null ? _input : _input = GetComponent<PlayerInput>();
        /// <summary>Stable 0-3 slot assigned on join. Drives colour, spawn point and research panel corner.</summary>
        public int Index => Input.playerIndex;
        public Color Color { get; private set; } = Color.white;
        public bool IsConnected { get; private set; } = true;
        public bool IsResearching { get; private set; }

        void OnEnable()
        {
            Input.onDeviceLost += HandleDeviceLost;
            Input.onDeviceRegained += HandleDeviceRegained;
        }

        void OnDisable()
        {
            Input.onDeviceLost -= HandleDeviceLost;
            Input.onDeviceRegained -= HandleDeviceRegained;
        }

        public void SetColor(Color color)
        {
            Color = color;
            ApplyTint(IsConnected ? color : DisconnectedTint(color));
        }

        public void EnterResearch()
        {
            IsResearching = true;
            Input.SwitchCurrentActionMap(InputNames.ResearchMap);
        }

        public void ExitResearch()
        {
            IsResearching = false;
            Input.SwitchCurrentActionMap(InputNames.PlayerMap);
        }

        // A disconnected controller keeps its slot: PlayerInput re-pairs the device automatically when it
        // comes back. Console certification expects this, plus a "reconnect controller" prompt (TODO: UI).
        void HandleDeviceLost(PlayerInput _)
        {
            IsConnected = false;
            ApplyTint(DisconnectedTint(Color));
            ConnectionChanged?.Invoke(this);
        }

        void HandleDeviceRegained(PlayerInput _)
        {
            IsConnected = true;
            ApplyTint(Color);
            ConnectionChanged?.Invoke(this);
        }

        static Color DisconnectedTint(Color color) => Color.Lerp(color, Color.gray, 0.75f);

        void ApplyTint(Color color)
        {
            _block ??= new MaterialPropertyBlock();
            foreach (var tinted in _tintedRenderers)
            {
                if (tinted == null) continue;
                tinted.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, color);
                tinted.SetPropertyBlock(_block);
            }
        }
    }
}
