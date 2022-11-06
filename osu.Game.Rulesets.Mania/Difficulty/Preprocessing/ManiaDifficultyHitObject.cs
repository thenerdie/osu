// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.Mania.Difficulty.Preprocessing
{
    public class ManiaDifficultyHitObject : DifficultyHitObject
    {
        public new ManiaHitObject BaseObject => (ManiaHitObject)base.BaseObject;

        public int AnchorCount;
        public int TrillCount;
        public double ColumnDelta;
        public double GreatHitWindow;

        public ManiaDifficultyHitObject(HitObject hitObject, HitObject lastObject, double clockRate, List<DifficultyHitObject> objects, int index, int anchorCount, int trillCount, double columnDelta)
            : base(hitObject, lastObject, clockRate, objects, index)
        {
            AnchorCount = anchorCount;
            TrillCount = trillCount;
            ColumnDelta = columnDelta;
        }
    }
}
