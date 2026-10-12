using System.Collections.Generic;
using UnityEngine;

namespace Blobs.Research.Minigames
{
    /// <summary>
    /// Placeholder research minigame: steer a probe around a walled arena and press Submit while touching a
    /// signal node to lock it. Lock every node to complete the research.
    ///
    /// It exists to prove the plumbing (per-player input, local 2D physics, panel camera, completion) rather
    /// than to be fun. Everything is built in code from primitives so there are no art dependencies; replace
    /// it with the real minigame. Visuals are deliberately big, flat and text-free so they read in a quarter
    /// of a TV (and 640x360 on Switch handheld).
    /// </summary>
    public class SignalTraceMinigame : ResearchMinigame
    {
        [SerializeField] Vector2 _arena = new(16f, 9f);
        [SerializeField] int _nodeCount = 5;
        [SerializeField] float _probeSpeed = 7f;
        [SerializeField] float _probeRadius = 0.45f;
        [SerializeField] float _nodeRadius = 0.5f;
        [SerializeField] Color _nodeColor = new(0.55f, 0.6f, 0.7f);
        [SerializeField] Color _wallColor = new(0.25f, 0.28f, 0.35f);

        readonly List<Collider2D> _overlaps = new();
        readonly HashSet<Collider2D> _openNodes = new();

        Rigidbody2D _probe;
        Transform _progressFill;
        ContactFilter2D _nodeFilter;

        public override Vector2 ArenaSize => _arena;

        protected override void OnBegin()
        {
            _nodeFilter = new ContactFilter2D { useTriggers = true };

            BuildWalls();
            _probe = BuildProbe();
            BuildNodes();
            _progressFill = BuildProgressBar();
            UpdateProgress();
        }

        protected override void OnFixedTick(float deltaTime)
        {
            _probe.linearVelocity = Vector2.ClampMagnitude(Context.Navigate.ReadValue<Vector2>(), 1f) * _probeSpeed;
        }

        protected override void OnTick(float deltaTime)
        {
            if (!Context.Submit.WasPressedThisFrame()) return;

            // Query this instance's physics scene, never Physics2D: other players' copies live in other scenes.
            _overlaps.Clear();
            Context.Physics.OverlapCircle(_probe.position, _probeRadius, _nodeFilter, _overlaps);
            foreach (var hit in _overlaps)
            {
                if (!_openNodes.Remove(hit)) continue;
                hit.enabled = false;
                hit.GetComponent<SpriteRenderer>().color = Context.PlayerColor;
            }

            UpdateProgress();
            if (_openNodes.Count == 0)
                Finish(ResearchOutcome.Completed);
        }

        void UpdateProgress()
        {
            var done = _nodeCount == 0 ? 1f : 1f - (float)_openNodes.Count / _nodeCount;
            _progressFill.localScale = new Vector3(_arena.x * done, 0.3f, 1f);
            _progressFill.localPosition = new Vector3(-_arena.x * 0.5f * (1f - done), _arena.y * 0.5f - 0.5f, 0f);
        }

        void BuildWalls()
        {
            const float thickness = 0.4f;
            var half = _arena * 0.5f;
            BuildWall("Wall Top", new Vector2(0f, half.y), new Vector2(_arena.x + thickness, thickness));
            BuildWall("Wall Bottom", new Vector2(0f, -half.y), new Vector2(_arena.x + thickness, thickness));
            BuildWall("Wall Left", new Vector2(-half.x, 0f), new Vector2(thickness, _arena.y + thickness));
            BuildWall("Wall Right", new Vector2(half.x, 0f), new Vector2(thickness, _arena.y + thickness));
        }

        void BuildWall(string wallName, Vector2 position, Vector2 size)
        {
            var wall = CreatePart(wallName, position, PlaceholderSprites.Square, _wallColor, 0);
            wall.transform.localScale = new Vector3(size.x, size.y, 1f);
            wall.AddComponent<BoxCollider2D>();
        }

        Rigidbody2D BuildProbe()
        {
            var probe = CreatePart("Probe", Vector2.zero, PlaceholderSprites.Circle, Context.PlayerColor, 10);
            probe.transform.localScale = Vector3.one * (_probeRadius * 2f);

            var body = probe.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            probe.AddComponent<CircleCollider2D>().radius = 0.5f;
            return body;
        }

        void BuildNodes()
        {
            // Seeded per player and per run so instances differ, but each layout is reproducible when debugging.
            var random = new System.Random(Context.Player.Index * 7919 + Time.frameCount);
            var inner = _arena * 0.5f - Vector2.one * 1.2f;

            for (var i = 0; i < _nodeCount; i++)
            {
                Vector2 position;
                do
                {
                    position = new Vector2(
                        Mathf.Lerp(-inner.x, inner.x, (float)random.NextDouble()),
                        Mathf.Lerp(-inner.y, inner.y * 0.8f, (float)random.NextDouble()));
                } while (position.sqrMagnitude < 4f); // keep clear of the probe's start

                var node = CreatePart($"Node {i + 1}", position, PlaceholderSprites.Circle, _nodeColor, 5);
                node.transform.localScale = Vector3.one * (_nodeRadius * 2f);
                var trigger = node.AddComponent<CircleCollider2D>();
                trigger.radius = 0.5f;
                trigger.isTrigger = true;
                _openNodes.Add(trigger);
            }
        }

        Transform BuildProgressBar()
        {
            var fill = CreatePart("Progress", Vector2.zero, PlaceholderSprites.Square, Context.PlayerColor, 20);
            return fill.transform;
        }

        /// <summary>
        /// New GameObjects are created in the active (3D) scene. Parenting them under this object moves them into
        /// the minigame scene, which must happen before adding physics components so they join the local
        /// physics scene.
        /// </summary>
        GameObject CreatePart(string partName, Vector2 localPosition, Sprite sprite, Color color, int sortingOrder)
        {
            var part = new GameObject(partName);
            part.transform.SetParent(transform, worldPositionStays: false);
            part.transform.localPosition = localPosition;

            var spriteRenderer = part.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = sprite;
            spriteRenderer.color = color;
            spriteRenderer.sortingOrder = sortingOrder;
            return part;
        }
    }
}
