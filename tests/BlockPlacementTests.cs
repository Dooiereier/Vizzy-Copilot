using System;
using System.Collections.Generic;
using Assets.Scripts;

public static class BlockPlacementTests
{
    private const float W = 100f;
    private const float H = 40f;
    private const float Gap = 20f;

    // 1500 wide, 1200 high: the target is at (1000, 600) with the left edge at 500.
    private static readonly PlacementBox View = new PlacementBox(500f, 0f, 2000f, 1200f);

    private static PlacementBox Find(List<PlacementBox> blocks, List<PlacementBox> columns) => BlockPlacement.Find(View, blocks, columns, W, H, Gap);

    public static void Run(Action<string, bool> check)
    {
        var none = new List<PlacementBox>();

        var empty = Find(none, none);
        check("an empty view: centered on the target", Math.Abs(empty.CenterX - 1000f) <= Gap && Math.Abs(empty.CenterY - 600f) <= Gap);

        var column = new PlacementBox(900f, 700f, 1300f, 900f);
        var under = Find(new List<PlacementBox> { column }, new List<PlacementBox> { column });
        check("below a column, left-aligned, a gap down", under.XMin == 900f && under.YMax == 680f);

        var far = new PlacementBox(1500f, 700f, 1900f, 900f);
        var nearest = Find(new List<PlacementBox> { far, column }, new List<PlacementBox> { far, column });
        check("the column nearest the target wins", nearest.XMin == 900f);

        var loose = new PlacementBox(950f, 640f, 1050f, 670f);
        var blocked = Find(new List<PlacementBox> { column, loose }, new List<PlacementBox> { column });
        check("a spot with something in it isn't used", !blocked.Overlaps(column, 0f) && !blocked.Overlaps(loose, 0f) && blocked.YMax != 680f);

        var underFlyout = new PlacementBox(100f, 700f, 1300f, 900f);
        var shifted = Find(new List<PlacementBox> { underFlyout }, new List<PlacementBox> { underFlyout });
        check("a column starting under the flyout: the spot is in view", shifted.XMin == 520f || shifted.IsInside(View));

        var ending = new PlacementBox(900f, -500f, 1300f, 900f);
        var past = Find(new List<PlacementBox> { ending }, new List<PlacementBox> { ending });
        check("a column running past the bottom isn't used, and the spot is free", past.IsInside(View) && !past.Overlaps(ending, 0f));

        var lonely = new PlacementBox(950f, 590f, 1050f, 610f);
        var beside = Find(new List<PlacementBox> { lonely }, none);
        check("a lone block, no column: a free spot near the target", !beside.Overlaps(lonely, Gap - 0.01f) && Math.Abs(beside.CenterX - 1000f) < 500f);

        var full = new List<PlacementBox>();
        for (var x = 500f; x < 2000f; x += 300f)
        {
            full.Add(new PlacementBox(x, 0f, x + 280f, 1200f));
        }

        var packed = Find(full, full);
        check("a full view: to the left of the column nearest the target's line, by the gap", packed.XMax == 1100f - Gap);
        check("a full view: halfway down", packed.CenterY == 600f);

        full.Add(packed);
        var again = BlockPlacement.Find(View, full, full, W, H, Gap, packed);
        check("a full view, again: not exactly on the last one", again.YMax < packed.YMax && again.XMin == packed.XMin);
    }
}
