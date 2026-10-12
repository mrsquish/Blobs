using UnityEngine;

namespace Blobs.Research
{
    /// <summary>
    /// The single root object of the research minigame scene. The session manager finds it after loading,
    /// moves it far away from the 3D world and from other instances, then points its camera at a screen panel.
    /// </summary>
    public class ResearchMinigameRoot : MonoBehaviour
    {
        [SerializeField] Camera _camera;
        [SerializeField] ResearchMinigame _minigame;
        [Tooltip("World units of breathing room around the arena inside the panel.")]
        [SerializeField] float _padding = 0.5f;

        public Camera Camera => _camera;
        public ResearchMinigame Minigame => _minigame;

        /// <summary>
        /// The camera is saved disabled so the first frame never shows whatever is at the world origin.
        /// It turns on once the instance has been placed and given a panel.
        /// </summary>
        public void ShowInPanel(Rect viewport, Color background)
        {
            _camera.rect = viewport;
            _camera.backgroundColor = background;
            _camera.enabled = true;
            FitArena();
        }

        void LateUpdate()
        {
            if (_camera.enabled)
                FitArena();
        }

        // Panels change shape (corner, half, quadrant), so keep the whole arena visible whatever the aspect.
        void FitArena()
        {
            var arena = _minigame.ArenaSize + Vector2.one * (_padding * 2f);
            _camera.orthographicSize = Mathf.Max(arena.y * 0.5f, arena.x * 0.5f / _camera.aspect);
        }
    }
}
