namespace Assets.Scripts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>A rectangle with y pointing up, as on the canvas in world space.</summary>
    public struct PlacementBox
    {
        private const float Tolerance = 0.01f;

        public PlacementBox(float xMin, float yMin, float xMax, float yMax)
        {
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        public float XMin { get; }

        public float YMin { get; }

        public float XMax { get; }

        public float YMax { get; }

        public float Width => XMax - XMin;

        public float Height => YMax - YMin;

        public float CenterX => (XMin + XMax) / 2f;

        public float CenterY => (YMin + YMax) / 2f;

        public bool Overlaps(PlacementBox other, float gap)
        {
            // A spot exactly one gap away is meant to count as clear, and rounding mustn't change that.
            gap = Math.Max(0f, gap - Tolerance);
            return XMin < other.XMax + gap && other.XMin < XMax + gap && YMin < other.YMax + gap && other.YMin < YMax + gap;
        }

        public bool IsInside(PlacementBox other)
        {
            return XMin >= other.XMin && XMax <= other.XMax && YMin >= other.YMin && YMax <= other.YMax;
        }
    }

    /// <summary>
    /// Where a block from the search lands, in what's visible of the canvas. The target is a third of the way across, halfway down.
    /// First choice: below a column near the target. Then a free spot near the target. With neither, to the left of the column
    /// nearest the target's line, halfway down. Plain logic, tested offline.
    /// </summary>
    public static class BlockPlacement
    {
        /// <summary>How far from the target still counts as near it, as a fraction of the visible size.</summary>
        private const float Reach = 0.3f;

        /// <param name="view">What's visible of the canvas.</param>
        /// <param name="blocks">Everything on the canvas: each stack and each loose block.</param>
        /// <param name="columns">The stacks a block can go below.</param>
        /// <param name="width">The new block's width.</param>
        /// <param name="height">The new block's height.</param>
        /// <param name="gap">The space to keep around the new block.</param>
        /// <param name="last">The block the search placed last, which a very full view doesn't line up with.</param>
        /// <returns>The new block's box.</returns>
        public static PlacementBox Find(PlacementBox view, IList<PlacementBox> blocks, IList<PlacementBox> columns, float width, float height, float gap, PlacementBox? last = null)
        {
            var targetX = view.XMin + (view.Width / 3f);
            var targetY = view.CenterY;

            bool IsNear(PlacementBox box) => Math.Abs(box.CenterX - targetX) <= view.Width * Reach && Math.Abs(box.CenterY - targetY) <= view.Height * Reach;
            var inView = blocks.Where(block => block.Overlaps(view, gap)).ToList();
            bool IsFree(PlacementBox box) => box.IsInside(view) && !inView.Any(block => box.Overlaps(block, gap));
            float Distance(PlacementBox box) => ((box.CenterX - targetX) * (box.CenterX - targetX)) + ((box.CenterY - targetY) * (box.CenterY - targetY));

            var below = columns
                .Where(column => column.YMin >= view.YMin && column.YMin <= view.YMax && column.XMax > view.XMin && column.XMin < view.XMax)
                .Select(column => BoxAt(Math.Max(column.XMin, view.XMin + gap), column.YMin - gap, width, height))
                .Where(box => IsNear(box) && IsFree(box))
                .OrderBy(Distance)
                .Cast<PlacementBox?>()
                .FirstOrDefault();
            if (below != null)
            {
                return below.Value;
            }

            var free = FreeSpotNearTarget(view, targetX, targetY, width, height, gap, IsNear, IsFree, Distance);
            return free ?? LeftOfNearestColumn(view, blocks, columns, last, targetX, targetY, width, height, gap);
        }

        private static PlacementBox? FreeSpotNearTarget(PlacementBox view, float targetX, float targetY, float width, float height, float gap, Func<PlacementBox, bool> isNear, Func<PlacementBox, bool> isFree, Func<PlacementBox, float> distance)
        {
            // Zoomed far out the gap is tiny, and the spots tried have to stay few: one key press, thousands of blocks.
            var step = Math.Max(Math.Max(gap, Math.Min(view.Width, view.Height) / 40f), 1f);
            var stepsX = (int)Math.Ceiling(view.Width * Reach / step);
            var stepsY = (int)Math.Ceiling(view.Height * Reach / step);
            PlacementBox? best = null;
            var bestDistance = float.MaxValue;
            for (var x = -stepsX; x <= stepsX; x++)
            {
                // From the bottom up, so of two spots equally near, the lower one wins.
                for (var y = -stepsY; y <= stepsY; y++)
                {
                    var box = BoxAt(targetX + (x * step) - (width / 2f), targetY + (y * step) + (height / 2f), width, height);
                    var boxDistance = distance(box);
                    if (boxDistance < bestDistance && isNear(box) && isFree(box))
                    {
                        best = box;
                        bestDistance = boxDistance;
                    }
                }
            }

            return best;
        }

        /// <summary>A very full view: overlapping can't be avoided, so somewhere predictable, shifted down if the last block is exactly there.</summary>
        private static PlacementBox LeftOfNearestColumn(PlacementBox view, IList<PlacementBox> blocks, IList<PlacementBox> columns, PlacementBox? last, float targetX, float targetY, float width, float height, float gap)
        {
            // Real columns first: a view full of loose blocks has none to line up with.
            bool IsCandidate(PlacementBox block) => block.Overlaps(view, 0f) && !(last != null && block.Equals(last.Value));
            var visible = columns.Where(IsCandidate).ToList();
            if (visible.Count == 0)
            {
                visible = blocks.Where(IsCandidate).ToList();
            }

            var left = visible.Count == 0
                ? targetX - (width / 2f)
                : visible.OrderBy(block => Math.Abs(block.XMin - targetX)).First().XMin - gap - width;
            left = Math.Max(view.XMin + gap, left);

            // A block taller than the view shows its top.
            var top = Math.Min(targetY + (height / 2f), view.YMax);
            while (blocks.Any(block => Math.Abs(block.XMin - left) < 1f && Math.Abs(block.YMax - top) < 1f) && top - height - (height + gap) >= view.YMin)
            {
                top -= height + gap;
            }

            return BoxAt(left, top, width, height);
        }

        private static PlacementBox BoxAt(float left, float top, float width, float height)
        {
            return new PlacementBox(left, top - height, left + width, top);
        }
    }
}
