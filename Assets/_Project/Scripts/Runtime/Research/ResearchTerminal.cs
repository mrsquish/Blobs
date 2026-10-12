using Blobs.Core;
using Blobs.Players;
using UnityEngine;

namespace Blobs.Research
{
    /// <summary>
    /// A console in the 3D world. Interacting starts a research session for that player; their character stays
    /// here (and vulnerable) while they play the 2D minigame in their screen panel.
    /// </summary>
    public class ResearchTerminal : MonoBehaviour, IInteractable
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] ResearchSessionManager _sessions;
        [SerializeField] Renderer _screen;
        [SerializeField] Color _idleColor = new(0.2f, 0.9f, 1f);

        MaterialPropertyBlock _block;
        CoopPlayer _user;

        public bool IsInUse => _user != null;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            SetScreenColor(_idleColor);
        }

        public bool TryInteract(CoopPlayer player)
        {
            if (IsInUse || _sessions == null)
                return false;
            if (!_sessions.TryBegin(player, this))
                return false;

            _user = player;
            SetScreenColor(player.Color);
            return true;
        }

        public void Release(CoopPlayer player)
        {
            if (_user != player) return;
            _user = null;
            SetScreenColor(_idleColor);
        }

        void SetScreenColor(Color color)
        {
            if (_screen == null) return;
            _screen.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _screen.SetPropertyBlock(_block);
        }
    }
}
