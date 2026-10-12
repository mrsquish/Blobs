using System.Collections.Generic;
using Blobs.Players;
using UnityEngine;

namespace Blobs.Cameras
{
    /// <summary>
    /// The couch camera: a single shared view while the group fits on screen, splitting dynamically (Voronoi
    /// split-screen) when players spread further apart than the camera can frame at a readable size.
    ///
    /// Every player slot owns a view camera, all with the same angle and lens. While merged, every view has the same
    /// focus, so only one camera renders. When split, players are clustered; each cluster's view slides just far
    /// enough to keep it on screen (<see cref="SplitScreenMath.SplitFocus"/>), and the composite shader shows each
    /// pixel from the view of the nearest player on screen, with a divider only where neighbouring views differ.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after players have moved this frame
    public class SplitScreenDirector : MonoBehaviour
    {
        public const int MaxViews = 4;

        static readonly int SitesId = Shader.PropertyToID("_Sites");
        static readonly int SiteCountId = Shader.PropertyToID("_SiteCount");
        static readonly int SeamsId = Shader.PropertyToID("_Seams");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");
        static readonly int[] ViewTextureIds =
        {
            Shader.PropertyToID("_View0"), Shader.PropertyToID("_View1"),
            Shader.PropertyToID("_View2"), Shader.PropertyToID("_View3"),
        };

        [SerializeField] CoopSession _session;
        [Tooltip("One per player slot. Slot 0 also renders straight to the screen in edit mode as a preview.")]
        [SerializeField] Camera[] _viewCameras = new Camera[MaxViews];
        [SerializeField] Camera _compositeCamera;
        [SerializeField] Transform _compositeQuad;
        [SerializeField] Material _compositeMaterial;

        [Header("View")]
        [SerializeField] float _pitch = 50f;
        [SerializeField] float _yaw;
        [SerializeField] float _fieldOfView = 40f;
        [SerializeField] float _minDistance = 14f;
        [Tooltip("Furthest the camera pulls back. Beyond this players would be too small, so the screen splits instead.")]
        [SerializeField] float _maxDistance = 40f;
        [Tooltip("Players are kept inside this part of the screen (viewport space) before the camera pulls back.")]
        [SerializeField] Rect _safeArea = new(0.1f, 0.1f, 0.8f, 0.8f);
        [SerializeField] float _playerHeight = 2f;
        [SerializeField] float _smoothTime = 0.25f;

        [Header("Nobody playing")]
        [SerializeField] Transform _idleFocus;
        [SerializeField] float _idleDistance = 22f;

        [Header("Splitting")]
        [Tooltip("When split, how far from the screen centre each group sits, as a fraction of the view's half-size. " +
                 "0.5 puts each of two players in the middle of their half of the screen.")]
        [SerializeField, Range(0.1f, 0.9f)] float _splitOffset = 0.5f;
        [Tooltip("Players closer than this (fraction of the max view's half-width) share a view when split.")]
        [SerializeField] float _mergeDistance = 0.6f;
        [Tooltip("Players sharing a view keep sharing it until this far apart (hysteresis).")]
        [SerializeField] float _splitDistance = 0.9f;
        [Tooltip("Extra margin (in screen heights) each split view renders beyond its section, covering the divider " +
                 "and texture filtering at the edges.")]
        [SerializeField] float _sectionPadding = 0.01f;

        readonly List<CoopPlayer> _players = new();
        readonly List<Vector3> _feet = new();
        readonly List<Vector3> _fitPoints = new();
        readonly List<Vector3> _clusterFeet = new();
        readonly Vector3[] _targetFocus = new Vector3[MaxViews];
        readonly float[] _targetDistance = new float[MaxViews];
        readonly Vector3[] _focus = new Vector3[MaxViews];
        readonly Vector3[] _focusVelocity = new Vector3[MaxViews];
        readonly float[] _distance = new float[MaxViews];
        readonly float[] _distanceVelocity = new float[MaxViews];
        readonly bool[] _active = new bool[MaxViews];
        readonly bool[] _initialised = new bool[MaxViews];
        readonly int[] _owner = new int[MaxViews];
        readonly RenderTexture[] _textures = new RenderTexture[MaxViews];
        readonly Vector4[] _sites = new Vector4[MaxViews];
        readonly Vector2[] _sitePositions = new Vector2[MaxViews];
        readonly Rect[] _viewRects = new Rect[MaxViews];
        int _siteCount;
        int[] _clusters;
        bool _compositing;

        public Quaternion ViewRotation => Quaternion.Euler(_pitch, _yaw, 0f);

        /// <summary>How many different views are on screen (1 = not split).</summary>
        public int DistinctViewCount { get; private set; } = 1;
        public bool IsSplit => DistinctViewCount > 1;

        /// <summary>
        /// Pixels rendered by the view cameras, as a multiple of one full screen. Each view draws only its own
        /// section, so this stays close to 1 however many ways the screen is split.
        /// </summary>
        public float RenderedScreenFraction { get; private set; } = 1f;

        /// <summary>The camera whose image this player currently appears in.</summary>
        public Camera CameraShowing(CoopPlayer player) => _viewCameras[_owner[player.Index % MaxViews]];

        void Start()
        {
            _compositing = true;
            _compositeCamera.enabled = true;
        }

        void OnDestroy()
        {
            for (var i = 0; i < MaxViews; i++)
            {
                if (_textures[i] == null) continue;
                _textures[i].Release();
                Destroy(_textures[i]);
            }
        }

        /// <summary>
        /// Renders the current composited frame (without research panels or IMGUI) into a texture of the screen's
        /// aspect ratio. For screenshots and visual tests.
        /// </summary>
        public void RenderTo(RenderTexture target)
        {
            foreach (var viewCamera in _viewCameras)
            {
                if (viewCamera != null && viewCamera.enabled)
                    viewCamera.Render();
            }

            var previous = _compositeCamera.targetTexture;
            _compositeCamera.targetTexture = target;
            _compositeCamera.Render();
            _compositeCamera.targetTexture = previous;
        }

        void LateUpdate()
        {
            var view = new ViewSettings(ViewRotation, _fieldOfView, ScreenAspect());
            GatherPlayers();
            ComputeTargets(view);
            Smooth();
            AssignOwners();
            ComputeSites(view);
            ComputeViewRects(view);
            ApplyCameras(view);
            UpdateComposite(view);
        }

        void GatherPlayers()
        {
            _players.Clear();
            if (_session != null)
            {
                foreach (var player in _session.Players)
                {
                    if (player != null && player.Index < MaxViews)
                        _players.Add(player);
                }
            }
            _players.Sort((a, b) => a.Index.CompareTo(b.Index));

            for (var slot = 0; slot < MaxViews; slot++)
                _active[slot] = false;
            foreach (var player in _players)
                _active[player.Index] = true;
        }

        void ComputeTargets(in ViewSettings view)
        {
            if (_players.Count == 0)
            {
                _active[0] = true;
                _targetFocus[0] = _idleFocus != null ? _idleFocus.position : Vector3.zero;
                _targetDistance[0] = _idleDistance;
                _clusters = null;
                return;
            }

            _feet.Clear();
            _fitPoints.Clear();
            foreach (var player in _players)
            {
                var feet = player.transform.position;
                _feet.Add(feet);
                _fitPoints.Add(feet);
                _fitPoints.Add(feet + Vector3.up * _playerHeight);
            }

            var centre = SplitScreenMath.GroundCentre(_feet);
            var fits = SplitScreenMath.TryFitDistance(_fitPoints, centre, view, _safeArea, _minDistance, _maxDistance, out var distance);

            // Clusters are tracked every frame, merged or not, so the hysteresis has history when a split begins.
            var halfExtents = SplitScreenMath.GroundHalfExtents(_maxDistance, view);
            _clusters = SplitScreenMath.Cluster(_feet, _clusters, halfExtents.x * _mergeDistance, halfExtents.x * _splitDistance);

            for (var i = 0; i < _players.Count; i++)
            {
                var slot = _players[i].Index;
                if (fits)
                {
                    _targetFocus[slot] = centre;
                    _targetDistance[slot] = distance;
                    continue;
                }

                _clusterFeet.Clear();
                for (var j = 0; j < _players.Count; j++)
                {
                    if (_clusters[j] == _clusters[i])
                        _clusterFeet.Add(_feet[j]);
                }

                var clusterCentre = SplitScreenMath.GroundCentre(_clusterFeet);
                _targetFocus[slot] = SplitScreenMath.SplitFocus(clusterCentre, centre, halfExtents * _splitOffset, view);
                _targetDistance[slot] = _maxDistance;
            }
        }

        void Smooth()
        {
            for (var slot = 0; slot < MaxViews; slot++)
            {
                if (!_active[slot])
                {
                    _initialised[slot] = false;
                    continue;
                }

                if (!_initialised[slot])
                {
                    // Start from an existing view so a joining player doesn't create a seam while it catches up.
                    var source = FirstInitialisedSlot();
                    if (source >= 0)
                    {
                        CopyViewState(source, slot);
                    }
                    else
                    {
                        _focus[slot] = _targetFocus[slot];
                        _distance[slot] = _targetDistance[slot];
                        _focusVelocity[slot] = Vector3.zero;
                        _distanceVelocity[slot] = 0f;
                    }
                    _initialised[slot] = true;
                }

                _focus[slot] = Vector3.SmoothDamp(_focus[slot], _targetFocus[slot], ref _focusVelocity[slot], _smoothTime);
                _distance[slot] = Mathf.SmoothDamp(_distance[slot], _targetDistance[slot], ref _distanceVelocity[slot], _smoothTime);
            }
        }

        int FirstInitialisedSlot()
        {
            for (var slot = 0; slot < MaxViews; slot++)
            {
                if (_active[slot] && _initialised[slot])
                    return slot;
            }
            return -1;
        }

        void CopyViewState(int from, int to)
        {
            _focus[to] = _focus[from];
            _distance[to] = _distance[from];
            _focusVelocity[to] = _focusVelocity[from];
            _distanceVelocity[to] = _distanceVelocity[from];
        }

        // Views that have converged on the same framing share one camera and render texture.
        void AssignOwners()
        {
            DistinctViewCount = 0;
            for (var slot = 0; slot < MaxViews; slot++)
            {
                _owner[slot] = slot;
                if (!_active[slot]) continue;

                for (var earlier = 0; earlier < slot; earlier++)
                {
                    if (!_active[earlier] || _owner[earlier] != earlier) continue;
                    var sameTarget = (_targetFocus[earlier] - _targetFocus[slot]).sqrMagnitude < 0.0025f
                                     && Mathf.Abs(_targetDistance[earlier] - _targetDistance[slot]) < 0.05f;
                    var sameView = (_focus[earlier] - _focus[slot]).sqrMagnitude < 0.0025f
                                   && Mathf.Abs(_distance[earlier] - _distance[slot]) < 0.05f;
                    if (sameView && sameTarget)
                    {
                        // Lock merged views together (velocity too) so damping can't drift them apart again.
                        CopyViewState(earlier, slot);
                        _owner[slot] = earlier;
                        break;
                    }
                }

                if (_owner[slot] == slot)
                    DistinctViewCount++;
            }
        }

        void ApplyCameras(in ViewSettings view)
        {
            var rotation = view.Rotation;
            RenderedScreenFraction = 0f;
            for (var slot = 0; slot < MaxViews; slot++)
            {
                var viewCamera = _viewCameras[slot];
                if (viewCamera == null) continue;

                var renders = _active[slot] && _owner[slot] == slot;
                viewCamera.enabled = renders;
                if (!renders) continue;

                viewCamera.transform.SetPositionAndRotation(SplitScreenMath.CameraPosition(_focus[slot], _distance[slot], view), rotation);
                viewCamera.fieldOfView = _fieldOfView;
                if (!_compositing) continue;

                var section = _viewRects[slot];
                if (section.width <= 0f || section.height <= 0f)
                {
                    // Its section is empty this frame (e.g. a group briefly off screen while views settle), so no
                    // pixel shows this view: skip rendering it entirely.
                    viewCamera.enabled = false;
                    continue;
                }

                viewCamera.targetTexture = TextureFor(slot);
                viewCamera.rect = section;
                if (section == FullScreen)
                {
                    viewCamera.ResetProjectionMatrix();
                }
                else
                {
                    // Render only this view's section of the screen, with exactly the pixels a full-screen render
                    // would put there, so the composite lines up.
                    var full = Matrix4x4.Perspective(_fieldOfView, view.Aspect, viewCamera.nearClipPlane, viewCamera.farClipPlane);
                    viewCamera.projectionMatrix = SplitScreenMath.CroppedProjection(full, section);
                }
                RenderedScreenFraction += section.width * section.height;
            }
        }

        RenderTexture TextureFor(int slot)
        {
            var width = Mathf.Max(16, Screen.width);
            var height = Mathf.Max(16, Screen.height);
            var texture = _textures[slot];
            if (texture != null && texture.width == width && texture.height == height)
                return texture;

            if (texture != null)
            {
                _viewCameras[slot].targetTexture = null;
                texture.Release();
                Destroy(texture);
            }

            texture = new RenderTexture(width, height, 24, RenderTextureFormat.Default) { name = $"Player View {slot + 1}" };
            texture.Create();
            return _textures[slot] = texture;
        }

        void ComputeSites(in ViewSettings view)
        {
            // One Voronoi site per distinct view, at the average on-screen position of the players it shows. (A site
            // per player would put kinks in the divider wherever two players share a view.)
            _siteCount = 0;
            if (_players.Count == 0)
            {
                _sites[_siteCount++] = new Vector4(0.5f, 0.5f, _owner[0], 0f);
            }
            else
            {
                for (var owner = 0; owner < MaxViews; owner++)
                {
                    if (!_active[owner] || _owner[owner] != owner) continue;

                    var sum = Vector2.zero;
                    var members = 0;
                    foreach (var player in _players)
                    {
                        if (_owner[player.Index] != owner) continue;
                        var body = player.transform.position + Vector3.up * (_playerHeight * 0.5f);
                        if (!SplitScreenMath.TryViewportPoint(body, _focus[owner], _distance[owner], view, out var onScreen))
                            onScreen = new Vector2(0.5f, 0.5f);
                        sum += onScreen;
                        members++;
                    }

                    var site = members > 0 ? sum / members : new Vector2(0.5f, 0.5f);
                    _sites[_siteCount++] = new Vector4(site.x, site.y, owner, 0f);
                }
            }
        }

        // Each view's section of the screen is its Voronoi cell; the camera renders only that cell's bounding box.
        void ComputeViewRects(in ViewSettings view)
        {
            for (var slot = 0; slot < MaxViews; slot++)
                _viewRects[slot] = FullScreen;
            if (_siteCount <= 1) return;

            for (var i = 0; i < _siteCount; i++)
                _sitePositions[i] = _sites[i];
            for (var i = 0; i < _siteCount; i++)
            {
                var owner = (int)_sites[i].z;
                _viewRects[owner] = SplitScreenMath.VoronoiCellBounds(_sitePositions, _siteCount, i, view.Aspect, _sectionPadding);
            }
        }

        void UpdateComposite(in ViewSettings view)
        {
            if (!_compositing) return;

            _compositeQuad.localScale = new Vector3(view.Aspect, 1f, 1f);
            for (var slot = 0; slot < MaxViews; slot++)
            {
                if (_textures[slot] != null)
                    _compositeMaterial.SetTexture(ViewTextureIds[slot], _textures[slot]);
            }

            var seams = Matrix4x4.zero;
            for (var a = 0; a < _siteCount; a++)
            {
                for (var b = 0; b < _siteCount; b++)
                {
                    var ownerA = (int)_sites[a].z;
                    var ownerB = (int)_sites[b].z;
                    // Fade the divider in as views separate, so splitting and merging don't pop.
                    seams[a, b] = ownerA == ownerB ? 0f : Mathf.Clamp01((_focus[ownerA] - _focus[ownerB]).magnitude / 2f);
                }
            }

            _compositeMaterial.SetVectorArray(SitesId, _sites);
            _compositeMaterial.SetFloat(SiteCountId, _siteCount);
            _compositeMaterial.SetMatrix(SeamsId, seams);
            _compositeMaterial.SetFloat(AspectId, view.Aspect);
        }

        static readonly Rect FullScreen = new(0f, 0f, 1f, 1f);

        static float ScreenAspect() => Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
    }
}
