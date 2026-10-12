using Blobs.Players;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blobs.UI
{
    /// <summary>
    /// "Press any button to join" prompt, shown while there are free player slots.
    /// Placeholder IMGUI so the prototype has no UI asset dependencies; replace with the real HUD.
    /// </summary>
    [RequireComponent(typeof(CoopSession), typeof(PlayerInputManager))]
    public class JoinPromptHud : MonoBehaviour
    {
        CoopSession _session;
        PlayerInputManager _manager;
        GUIStyle _style;

        void Awake()
        {
            _session = GetComponent<CoopSession>();
            _manager = GetComponent<PlayerInputManager>();
        }

        void OnGUI()
        {
            var joined = _session.Players.Count;
            var max = _manager.maxPlayerCount;
            if (max > 0 && joined >= max) return;

            _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            _style.fontSize = Mathf.RoundToInt(Screen.height * 0.04f); // sized for reading from the couch

            var text = joined == 0 ? "Press any button to join" : $"Press any button to join  ({joined}/{max})";
            text += $"\n<size={_style.fontSize * 2 / 3}>Keyboard: WASD side or arrow-key side</size>";
            var area = new Rect(0f, Screen.height * 0.8f, Screen.width, Screen.height * 0.14f);

            _style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(area.x + 2f, area.y + 2f, area.width, area.height), text, _style);
            _style.normal.textColor = Color.white;
            GUI.Label(area, text, _style);
        }
    }
}
