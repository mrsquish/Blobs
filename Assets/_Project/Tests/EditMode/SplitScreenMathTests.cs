using Blobs.Cameras;
using NUnit.Framework;
using UnityEngine;

namespace Blobs.Tests
{
    public class SplitScreenMathTests
    {
        static readonly ViewSettings View = new(Quaternion.Euler(50f, 0f, 0f), 40f, 16f / 9f);
        static readonly Rect SafeArea = new(0.1f, 0.1f, 0.8f, 0.8f);

        [Test]
        public void Focus_IsInTheMiddleOfTheView()
        {
            var focus = new Vector3(12f, 0f, -3f);

            Assert.That(SplitScreenMath.TryViewportPoint(focus, focus, 20f, View, out var viewport), Is.True);
            Assert.That(viewport.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(viewport.y, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void ViewportPoint_MatchesARealCamera()
        {
            var go = new GameObject("Test Camera");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.fieldOfView = View.FieldOfView;
                camera.aspect = View.Aspect;
                var focus = new Vector3(3f, 0f, 4f);
                go.transform.SetPositionAndRotation(SplitScreenMath.CameraPosition(focus, 25f, View), View.Rotation);

                var point = new Vector3(10f, 2f, 9f);
                var expected = camera.WorldToViewportPoint(point);
                SplitScreenMath.TryViewportPoint(point, focus, 25f, View, out var actual);

                Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-3f));
                Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-3f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FitDistance_PullsBackAsPlayersSpreadOut_ThenGivesUp()
        {
            float FitFor(float spread)
            {
                var points = new[] { new Vector3(-spread, 0f, 0f), new Vector3(spread, 0f, 0f) };
                return SplitScreenMath.TryFitDistance(points, Vector3.zero, View, SafeArea, 14f, 40f, out var distance) ? distance : -1f;
            }

            Assert.That(FitFor(1f), Is.EqualTo(14f), "close players use the closest view");
            Assert.That(FitFor(15f), Is.GreaterThan(14f).And.LessThan(40f));
            Assert.That(FitFor(15f), Is.GreaterThan(FitFor(12f)));
            Assert.That(FitFor(60f), Is.EqualTo(-1f), "too far apart to fit: time to split");
        }

        [Test]
        public void Cluster_GroupsChainsOfNearbyPlayers()
        {
            var positions = new[] { new Vector3(0f, 0f, 0f), new Vector3(8f, 0f, 0f), new Vector3(16f, 0f, 0f), new Vector3(60f, 0f, 0f) };

            var ids = SplitScreenMath.Cluster(positions, null, mergeDistance: 10f, splitDistance: 15f);

            Assert.That(ids, Is.EqualTo(new[] { 0, 0, 0, 1 }));
        }

        [Test]
        public void Cluster_HysteresisKeepsAGroupTogetherUntilTheSplitDistance()
        {
            var positions = new[] { Vector3.zero, new Vector3(12f, 0f, 0f) };

            Assert.That(SplitScreenMath.Cluster(positions, null, 10f, 15f), Is.EqualTo(new[] { 0, 1 }), "12m apart: too far to merge");
            Assert.That(SplitScreenMath.Cluster(positions, new[] { 0, 0 }, 10f, 15f), Is.EqualTo(new[] { 0, 0 }), "12m apart: close enough to stay together");
        }

        [Test]
        public void VoronoiCellBounds_OneSite_IsTheWholeScreen()
        {
            var bounds = SplitScreenMath.VoronoiCellBounds(new[] { new Vector2(0.3f, 0.7f) }, 1, 0, 16f / 9f, 0f);

            AssertRect(bounds, new Rect(0f, 0f, 1f, 1f));
        }

        [Test]
        public void VoronoiCellBounds_TwoSitesSideBySide_AreTheTwoHalves()
        {
            var sites = new[] { new Vector2(0.25f, 0.5f), new Vector2(0.75f, 0.5f) };

            AssertRect(SplitScreenMath.VoronoiCellBounds(sites, 2, 0, 16f / 9f, 0f), new Rect(0f, 0f, 0.5f, 1f));
            AssertRect(SplitScreenMath.VoronoiCellBounds(sites, 2, 1, 16f / 9f, 0f), new Rect(0.5f, 0f, 0.5f, 1f));
        }

        [Test]
        public void VoronoiCellBounds_FourCorners_AreQuadrants_AndPaddingGrowsThem()
        {
            var sites = new[] { new Vector2(0.25f, 0.75f), new Vector2(0.75f, 0.75f), new Vector2(0.25f, 0.25f), new Vector2(0.75f, 0.25f) };

            AssertRect(SplitScreenMath.VoronoiCellBounds(sites, 4, 3, 1f, 0f), new Rect(0.5f, 0f, 0.5f, 0.5f));
            AssertRect(SplitScreenMath.VoronoiCellBounds(sites, 4, 3, 1f, 0.01f), Rect.MinMaxRect(0.49f, 0f, 1f, 0.51f));
        }

        [Test]
        public void VoronoiCellBounds_SiteWhoseCellMissesTheScreen_IsEmpty()
        {
            // The second site is off screen, behind the first: every on-screen point is nearer the first.
            var sites = new[] { new Vector2(0.9f, 0.5f), new Vector2(1.6f, 0.5f) };

            var bounds = SplitScreenMath.VoronoiCellBounds(sites, 2, 1, 1f, 0.01f);

            Assert.That(bounds.width * bounds.height, Is.EqualTo(0f));
        }

        [Test]
        public void CroppedProjection_RendersTheSamePixelsAsTheFullProjection()
        {
            var full = Matrix4x4.Perspective(40f, 16f / 9f, 0.3f, 1000f);
            var section = new Rect(0.5f, 0.1f, 0.4f, 0.6f);
            var cropped = SplitScreenMath.CroppedProjection(full, section);
            var viewSpacePoint = new Vector4(2f, -1f, -20f, 1f); // in front of the camera (view space looks down -z)

            static Vector2 ToViewport(Matrix4x4 projection, Vector4 point)
            {
                var clip = projection * point;
                return new Vector2(clip.x / clip.w * 0.5f + 0.5f, clip.y / clip.w * 0.5f + 0.5f);
            }

            var onScreen = ToViewport(full, viewSpacePoint);
            var inSection = ToViewport(cropped, viewSpacePoint);
            var mappedBack = section.min + Vector2.Scale(inSection, section.size);

            Assert.That(mappedBack.x, Is.EqualTo(onScreen.x).Within(1e-5f));
            Assert.That(mappedBack.y, Is.EqualTo(onScreen.y).Within(1e-5f));
        }

        static void AssertRect(Rect actual, Rect expected)
        {
            Assert.That(actual.xMin, Is.EqualTo(expected.xMin).Within(1e-4f), "xMin");
            Assert.That(actual.xMax, Is.EqualTo(expected.xMax).Within(1e-4f), "xMax");
            Assert.That(actual.yMin, Is.EqualTo(expected.yMin).Within(1e-4f), "yMin");
            Assert.That(actual.yMax, Is.EqualTo(expected.yMax).Within(1e-4f), "yMax");
        }

        [Test]
        public void SplitFocus_LeavesNearbyGroupsOnTheSharedView()
        {
            var groupCentre = new Vector3(5f, 0f, 5f);
            var clusterCentre = groupCentre + new Vector3(3f, 0f, 1f);

            var focus = SplitScreenMath.SplitFocus(clusterCentre, groupCentre, new Vector2(10f, 10f), View);

            Assert.That(Vector3.Distance(focus, groupCentre), Is.LessThan(1e-4f));
        }

        [Test]
        public void SplitFocus_SlidesTheViewSoAFarGroupSitsAtTheLimit()
        {
            var groupCentre = Vector3.zero;
            var clusterCentre = new Vector3(50f, 0f, 0f);

            var focus = SplitScreenMath.SplitFocus(clusterCentre, groupCentre, new Vector2(10f, 10f), View);

            Assert.That(focus.x, Is.EqualTo(40f).Within(1e-3f), "cluster should end up 10m (the limit) right of its view's focus");
            Assert.That(focus.z, Is.EqualTo(0f).Within(1e-3f));
        }
    }
}
