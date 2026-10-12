using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace Blobs.Players
{
    /// <summary>
    /// Decides who joins when a button is pressed: an unpaired gamepad joins as a new player, and each half of
    /// the keyboard (WASD side / arrow-key side) can join as its own player.
    ///
    /// Replaces PlayerInputManager's built-in "join when button is pressed", which pairs a whole device to one
    /// player and so can never put two players on one keyboard. PlayerInputManager must be set to join manually.
    /// </summary>
    [RequireComponent(typeof(PlayerInputManager))]
    public class LocalJoinController : MonoBehaviour
    {
        [Tooltip("Control schemes that each claim part of the keyboard. Their key bindings must not overlap.")]
        [SerializeField] string[] _keyboardSchemes = { "KeyboardLeft", "KeyboardRight" };
        [Tooltip("Allow more than one player on the keyboard. Useful for development; harmless on consoles, " +
                 "which have no keyboard.")]
        [SerializeField] bool _allowSharedKeyboard = true;

        PlayerInputManager _manager;
        InputActionAsset _actions;
        IDisposable _buttonListener;

        void Awake()
        {
            _manager = GetComponent<PlayerInputManager>();
            _actions = _manager.playerPrefab.GetComponent<PlayerInput>().actions;
        }

        void OnEnable() => _buttonListener = InputSystem.onAnyButtonPress.Call(HandleButtonPress);

        void OnDisable()
        {
            _buttonListener?.Dispose();
            _buttonListener = null;
        }

        void HandleButtonPress(InputControl control)
        {
            var full = _manager.maxPlayerCount >= 0 && _manager.playerCount >= _manager.maxPlayerCount;
            if (full) return;

            switch (control.device)
            {
                case Keyboard keyboard:
                    TryJoinKeyboard(keyboard, control);
                    break;
                case Gamepad gamepad when PlayerInput.FindFirstPairedToDevice(gamepad) == null:
                    _manager.JoinPlayer(pairWithDevice: gamepad);
                    break;
            }
        }

        void TryJoinKeyboard(Keyboard keyboard, InputControl key)
        {
            var scheme = SchemeOwningKey(key);
            if (scheme == null || IsSchemeInUse(scheme))
                return;
            if (!_allowSharedKeyboard && PlayerInput.FindFirstPairedToDevice(keyboard) != null)
                return;

            _manager.JoinPlayer(controlScheme: scheme, pairWithDevice: keyboard);
        }

        /// <summary>Which keyboard scheme the pressed key belongs to, read from the action asset's bindings.</summary>
        string SchemeOwningKey(InputControl key)
        {
            foreach (var binding in _actions.bindings)
            {
                if (binding.isComposite || string.IsNullOrEmpty(binding.groups)) continue;
                if (!InputControlPath.Matches(binding.path, key)) continue;

                foreach (var scheme in _keyboardSchemes)
                {
                    if (Array.IndexOf(binding.groups.Split(InputBinding.Separator), scheme) >= 0)
                        return scheme;
                }
            }
            return null;
        }

        static bool IsSchemeInUse(string scheme)
        {
            foreach (var player in PlayerInput.all)
            {
                if (player.currentControlScheme == scheme)
                    return true;
            }
            return false;
        }
    }
}
