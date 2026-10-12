using System.Collections.Generic;
using UnityEngine;

namespace Blobs.Cameras
{
    /// <summary>Orientation and lens shared by every view camera.</summary>
    public readonly struct ViewSettings
    {
        public ViewSettings(Quaternion rotation, float fieldOfView, float aspect)
        {
            Rotation = rotation;
            FieldOfView = fieldOfView;
            Aspect = aspect;
        }

        public Quaternion Rotation { get; }
        /// <summary>Vertical field of view in degrees.</summary>
        public float FieldOfView { get; }
        public float Aspect { get; }

        /// <summary>Camera right, flattened onto the ground.</summary>
        public Vector3 GroundRight => Vector3.ProjectOnPlane(Rotation * Vector3.right, Vector3.up).normalized;
        /// <summary>Camera forward, flattened onto the ground ("up the screen").</summary>
        public Vector3 GroundForward => Vector3.ProjectOnPlane(Rotation * Vector3.forward, Vector3.up).normalized;

        public Vector2 ToGround(Vector3 offset) => new(Vector3.Dot(offset, GroundRight), Vector3.Dot(offset, GroundForward));
        public Vector3 FromGround(Vector2 offset) => GroundRight * offset.x + GroundForward * offset.y;
    }

    /// <summary>
    /// Pure maths behind the dynamic split-screen. A view is a camera with the shared orientation, looking at a
    /// focus point from some distance. Kept free of scene state so it can be unit tested.
    /// </summary>
    public static class SplitScreenMath
    {
        public static Vector3 CameraPosition(Vector3 focus, float distance, in ViewSettings view) =>
            focus - view.Rotation * Vector3.forward * distance;

        /// <summary>Where a world point lands in a view (0-1 viewport space). False if it is behind the camera.</summary>
        public static bool TryViewportPoint(Vector3 world, Vector3 focus, float distance, in ViewSettings view, out Vector2 viewport)
        {
            var local = Quaternion.Inverse(view.Rotation) * (world - CameraPosition(focus, distance, view));
            if (local.z <= 0.01f)
            {
                viewport = default;
                return false;
            }

            var tanHalf = Mathf.Tan(view.FieldOfView * 0.5f * Mathf.Deg2Rad);
            viewport = new Vector2(
                0.5f + 0.5f * local.x / (local.z * tanHalf * view.Aspect),
                0.5f + 0.5f * local.y / (local.z * tanHalf));
            return true;
        }

        public static bool AllInside(IReadOnlyList<Vector3> points, Vector3 focus, float distance, in ViewSettings view, Rect safeArea)
        {
            foreach (var point in points)
            {
                if (!TryViewportPoint(point, focus, distance, view, out var viewport) || !safeArea.Contains(viewport))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The closest distance (within min..max) at which every point sits inside the safe area. Returns false,
        /// with <paramref name="distance"/> = max, when even the furthest view cannot fit them: time to split.
        /// </summary>
        public static bool TryFitDistance(IReadOnlyList<Vector3> points, Vector3 focus, in ViewSettings view, Rect safeArea,
            float minDistance, float maxDistance, out float distance)
        {
            distance = maxDistance;
            if (AllInside(points, focus, minDistance, view, safeArea))
            {
                distance = minDistance;
                return true;
            }
            if (!AllInside(points, focus, maxDistance, view, safeArea))
                return false;

            // Moving the camera back only ever pulls points towards the centre, so the test is monotonic.
            var low = minDistance;
            var high = maxDistance;
            for (var i = 0; i < 20; i++)
            {
                var middle = (low + high) * 0.5f;
                if (AllInside(points, focus, middle, view, safeArea))
                    high = middle;
                else
                    low = middle;
            }
            distance = high;
            return true;
        }

        /// <summary>Centre of the points' bounding box on the ground (average height).</summary>
        public static Vector3 GroundCentre(IReadOnlyList<Vector3> points)
        {
            if (points.Count == 0) return Vector3.zero;

            var min = points[0];
            var max = points[0];
            var height = 0f;
            foreach (var point in points)
            {
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
                height += point.y;
            }
            return new Vector3((min.x + max.x) * 0.5f, height / points.Count, (min.z + max.z) * 0.5f);
        }

        /// <summary>
        /// Groups players that are close on the ground (single linkage, so chains count). Hysteresis stops groups
        /// flickering: players already together stay together until <paramref name="splitDistance"/> apart, and
        /// separate players only join at <paramref name="mergeDistance"/>.
        /// </summary>
        /// <param name="previous">Last frame's cluster id per player (same order), or null.</param>
        /// <returns>Cluster id per player, numbered 0.. in order of first appearance.</returns>
        public static int[] Cluster(IReadOnlyList<Vector3> positions, IReadOnlyList<int> previous, float mergeDistance, float splitDistance)
        {
            var count = positions.Count;
            var parent = new int[count];
            for (var i = 0; i < count; i++)
                parent[i] = i;

            int Find(int i)
            {
                while (parent[i] != i)
                    i = parent[i] = parent[parent[i]];
                return i;
            }

            for (var a = 0; a < count; a++)
            {
                for (var b = a + 1; b < count; b++)
                {
                    var wereTogether = previous != null && previous.Count == count && previous[a] >= 0 && previous[a] == previous[b];
                    var limit = wereTogether ? splitDistance : mergeDistance;
                    var offset = positions[a] - positions[b];
                    if (new Vector2(offset.x, offset.z).sqrMagnitude < limit * limit)
                        parent[Find(a)] = Find(b);
                }
            }

            var ids = new int[count];
            var labels = new Dictionary<int, int>();
            for (var i = 0; i < count; i++)
            {
                var root = Find(i);
                if (!labels.TryGetValue(root, out var label))
                    labels[root] = label = labels.Count;
                ids[i] = label;
            }
            return ids;
        }

        /// <summary>Scales an offset back onto the ellipse with the given radii if it lies outside it.</summary>
        public static Vector2 ClampToEllipse(Vector2 offset, Vector2 radii)
        {
            var normalised = new Vector2(offset.x / radii.x, offset.y / radii.y).magnitude;
            return normalised <= 1f ? offset : offset / normalised;
        }

        /// <summary>
        /// Half-size, on the ground, of what a view covers around its focus (approximate: the true footprint is a
        /// trapezoid because the camera is pitched).
        /// </summary>
        public static Vector2 GroundHalfExtents(float distance, in ViewSettings view)
        {
            var tanHalf = Mathf.Tan(view.FieldOfView * 0.5f * Mathf.Deg2Rad);
            var pitch = Vector3.Angle(view.Rotation * Vector3.forward, view.GroundForward) * Mathf.Deg2Rad;
            var sinPitch = Mathf.Max(0.2f, Mathf.Sin(pitch));
            return new Vector2(distance * tanHalf * view.Aspect, distance * tanHalf / sinPitch);
        }

        /// <summary>
        /// Bounding box, in viewport space, of one site's Voronoi cell on the screen, grown by
        /// <paramref name="padding"/> (in screen heights) and clamped to the screen. Distances are measured with the
        /// aspect ratio applied, matching the composite shader.
        /// </summary>
        public static Rect VoronoiCellBounds(Vector2[] sites, int count, int index, float aspect, float padding)
        {
            // Clip the screen rectangle by the half-plane "closer to this site than to that one" for every other site.
            // Each clip adds at most one vertex: 4 corners + 3 other sites fits easily in 16.
            System.Span<Vector2> polygon = stackalloc Vector2[16];
            System.Span<Vector2> clipped = stackalloc Vector2[16];
            polygon[0] = new Vector2(0f, 0f);
            polygon[1] = new Vector2(aspect, 0f);
            polygon[2] = new Vector2(aspect, 1f);
            polygon[3] = new Vector2(0f, 1f);
            var vertexCount = 4;

            var site = new Vector2(sites[index].x * aspect, sites[index].y);
            for (var other = 0; other < count && vertexCount > 0; other++)
            {
                if (other == index) continue;
                var otherSite = new Vector2(sites[other].x * aspect, sites[other].y);
                var normal = otherSite - site;
                if (normal.sqrMagnitude < 1e-10f) continue;

                var midpoint = (site + otherSite) * 0.5f;
                var clippedCount = 0;
                for (var i = 0; i < vertexCount; i++)
                {
                    var current = polygon[i];
                    var next = polygon[(i + 1) % vertexCount];
                    var currentSide = Vector2.Dot(current - midpoint, normal);
                    var nextSide = Vector2.Dot(next - midpoint, normal);
                    if (currentSide <= 0f)
                        clipped[clippedCount++] = current;
                    if ((currentSide < 0f && nextSide > 0f) || (currentSide > 0f && nextSide < 0f))
                        clipped[clippedCount++] = current + (next - current) * (currentSide / (currentSide - nextSide));
                }

                clipped.Slice(0, clippedCount).CopyTo(polygon);
                vertexCount = clippedCount;
            }

            if (vertexCount == 0)
                return new Rect(sites[index].x, sites[index].y, 0f, 0f);

            var min = polygon[0];
            var max = polygon[0];
            for (var i = 1; i < vertexCount; i++)
            {
                min = Vector2.Min(min, polygon[i]);
                max = Vector2.Max(max, polygon[i]);
            }

            var xMin = Mathf.Clamp01((min.x - padding) / aspect);
            var xMax = Mathf.Clamp01((max.x + padding) / aspect);
            var yMin = Mathf.Clamp01(min.y - padding);
            var yMax = Mathf.Clamp01(max.y + padding);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        /// <summary>
        /// A projection that renders only <paramref name="viewport"/> (a sub-rectangle of the screen) but with
        /// exactly the image the full <paramref name="projection"/> would put there. Pair it with Camera.rect.
        /// </summary>
        public static Matrix4x4 CroppedProjection(Matrix4x4 projection, Rect viewport)
        {
            var scaleX = 1f / viewport.width;
            var scaleY = 1f / viewport.height;
            var crop = Matrix4x4.identity;
            crop.m00 = scaleX;
            crop.m03 = -(viewport.center.x * 2f - 1f) * scaleX;
            crop.m11 = scaleY;
            crop.m13 = -(viewport.center.y * 2f - 1f) * scaleY;
            return crop * projection;
        }

        /// <summary>
        /// Focus for one cluster of a split screen. The merged view (looking at <paramref name="groupCentre"/>) would
        /// show the cluster at some offset from the screen centre; that offset is clamped so the cluster stays on
        /// screen, and the camera slides by whatever was clamped away. Clusters still within the limit get exactly
        /// the merged focus, so their views stay identical and render as one seamless picture.
        /// </summary>
        public static Vector3 SplitFocus(Vector3 clusterCentre, Vector3 groupCentre, Vector2 offsetLimit, in ViewSettings view)
        {
            var offset = view.ToGround(clusterCentre - groupCentre);
            var kept = ClampToEllipse(offset, offsetLimit);
            var focus = clusterCentre - view.FromGround(kept);
            focus.y = groupCentre.y;
            return focus;
        }
    }
}
