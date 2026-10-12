using Blobs.Research;
using NUnit.Framework;
using UnityEngine;

namespace Blobs.Tests
{
    public class ResearchPanelLayoutTests
    {
        static readonly Rect FullScreen = new(0f, 0f, 1f, 1f);

        [Test]
        public void SomeoneExploring_ResearchersGetTheirOwnCorner()
        {
            var researching = new[] { 0, 3 };

            var topLeft = ResearchPanelLayout.Calculate(0, researching, joinedPlayerCount: 4);
            var bottomRight = ResearchPanelLayout.Calculate(3, researching, joinedPlayerCount: 4);

            Assert.That(topLeft.xMin, Is.LessThan(0.5f));
            Assert.That(topLeft.yMin, Is.GreaterThan(0.5f));
            Assert.That(bottomRight.xMin, Is.GreaterThan(0.5f));
            Assert.That(bottomRight.yMax, Is.LessThan(0.5f));
        }

        [Test]
        public void CornerPanels_StayOnScreenAndNeverOverlap()
        {
            for (var a = 0; a < 4; a++)
            {
                var panel = ResearchPanelLayout.CornerPanel(a);
                Assert.That(FullScreen.Contains(panel.min) && FullScreen.Contains(panel.max), Is.True, $"slot {a} off screen");

                for (var b = a + 1; b < 4; b++)
                    Assert.That(panel.Overlaps(ResearchPanelLayout.CornerPanel(b)), Is.False, $"slots {a} and {b} overlap");
            }
        }

        [Test]
        public void SoloPlayerResearching_GetsFullScreen()
        {
            var rect = ResearchPanelLayout.Calculate(2, new[] { 2 }, joinedPlayerCount: 1);

            Assert.That(rect, Is.EqualTo(FullScreen));
        }

        [Test]
        public void TwoPlayersBothResearching_SplitLeftRightByLowerSlot()
        {
            var researching = new[] { 3, 1 };

            var lower = ResearchPanelLayout.Calculate(1, researching, joinedPlayerCount: 2);
            var higher = ResearchPanelLayout.Calculate(3, researching, joinedPlayerCount: 2);

            Assert.That(lower, Is.EqualTo(new Rect(0f, 0f, 0.5f, 1f)));
            Assert.That(higher, Is.EqualTo(new Rect(0.5f, 0f, 0.5f, 1f)));
        }

        [Test]
        public void FourPlayersAllResearching_GetQuadrants()
        {
            var researching = new[] { 0, 1, 2, 3 };

            Assert.That(ResearchPanelLayout.Calculate(0, researching, 4), Is.EqualTo(new Rect(0f, 0.5f, 0.5f, 0.5f)));
            Assert.That(ResearchPanelLayout.Calculate(1, researching, 4), Is.EqualTo(new Rect(0.5f, 0.5f, 0.5f, 0.5f)));
            Assert.That(ResearchPanelLayout.Calculate(2, researching, 4), Is.EqualTo(new Rect(0f, 0f, 0.5f, 0.5f)));
            Assert.That(ResearchPanelLayout.Calculate(3, researching, 4), Is.EqualTo(new Rect(0.5f, 0f, 0.5f, 0.5f)));
        }
    }
}
