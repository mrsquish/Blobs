using System.Collections.Generic;
using UnityEngine;

namespace Blobs.Research
{
    /// <summary>
    /// Works out where each researching player's 2D panel sits on the shared screen, in normalised viewport
    /// coordinates (0,0 = bottom-left). Pure so it can be unit tested and tuned without entering Play mode.
    ///
    /// Rules:
    /// - While anyone is still exploring, researchers get a corner panel (slot 0 top-left, 1 top-right,
    ///   2 bottom-left, 3 bottom-right) so the 3D view stays readable for everyone else.
    /// - When nobody is exploring, the panels grow: 1 researcher = full screen, 2 = left/right halves,
    ///   3-4 = quadrants by slot.
    /// </summary>
    public static class ResearchPanelLayout
    {
        public const float CornerPanelSize = 0.42f;
        public const float CornerMargin = 0.02f;

        static readonly Rect FullScreen = new(0f, 0f, 1f, 1f);

        public static Rect Calculate(int slot, IReadOnlyList<int> researchingSlots, int joinedPlayerCount)
        {
            var researcherCount = researchingSlots.Count;
            var someoneExploring = researcherCount < joinedPlayerCount;
            if (someoneExploring)
                return CornerPanel(slot);

            switch (researcherCount)
            {
                case 1:
                    return FullScreen;
                case 2:
                    // Order by slot so the lower slot is always on the left, whichever corners they own.
                    var leftSide = OrderOf(slot, researchingSlots) == 0;
                    return new Rect(leftSide ? 0f : 0.5f, 0f, 0.5f, 1f);
                default:
                    return Quadrant(slot);
            }
        }

        public static Rect Quadrant(int slot)
        {
            var column = slot % 2;
            var topRow = slot % 4 < 2;
            return new Rect(column * 0.5f, topRow ? 0.5f : 0f, 0.5f, 0.5f);
        }

        public static Rect CornerPanel(int slot)
        {
            var leftColumn = slot % 2 == 0;
            var topRow = slot % 4 < 2;
            var x = leftColumn ? CornerMargin : 1f - CornerMargin - CornerPanelSize;
            var y = topRow ? 1f - CornerMargin - CornerPanelSize : CornerMargin;
            return new Rect(x, y, CornerPanelSize, CornerPanelSize);
        }

        static int OrderOf(int slot, IReadOnlyList<int> slots)
        {
            var order = 0;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i] < slot)
                    order++;
            }
            return order;
        }
    }
}
