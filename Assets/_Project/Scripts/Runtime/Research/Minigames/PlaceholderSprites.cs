using UnityEngine;

namespace Blobs.Research.Minigames
{
    /// <summary>
    /// 1x1 world-unit sprites generated at runtime so the prototype has no art dependencies.
    /// Created once and shared by every minigame instance (they are read-only, so sharing is safe).
    /// </summary>
    public static class PlaceholderSprites
    {
        const int Resolution = 64;

        static Sprite _square;
        static Sprite _circle;

        public static Sprite Square => _square != null ? _square : _square = Create("Placeholder Square", circle: false);
        public static Sprite Circle => _circle != null ? _circle : _circle = Create("Placeholder Circle", circle: true);

        static Sprite Create(string spriteName, bool circle)
        {
            var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, mipChain: false)
            {
                name = spriteName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[Resolution * Resolution];
            var centre = (Resolution - 1) * 0.5f;
            var radius = Resolution * 0.5f;
            for (var y = 0; y < Resolution; y++)
            {
                for (var x = 0; x < Resolution; x++)
                {
                    // One pixel of soft edge so circles do not alias badly in small panels.
                    var distance = new Vector2(x - centre, y - centre).magnitude;
                    var alpha = circle ? Mathf.Clamp01(radius - distance) : 1f;
                    pixels[y * Resolution + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            var sprite = Sprite.Create(texture, new Rect(0, 0, Resolution, Resolution), new Vector2(0.5f, 0.5f), Resolution);
            sprite.name = spriteName;
            return sprite;
        }
    }
}
