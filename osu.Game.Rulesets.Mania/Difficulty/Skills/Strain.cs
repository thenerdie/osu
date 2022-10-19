// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Utils;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mania.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Framework.Logging;
//using System.Collections.Generic;

namespace osu.Game.Rulesets.Mania.Difficulty.Skills
{
    public class Strain : StrainDecaySkill
    {
        private const double individual_decay_base = 0.125;
        private const double overall_decay_base = 0.30;
        private const double release_threshold = 24;

        protected override double SkillMultiplier => 1;
        protected override double StrainDecayBase => 1;

        private readonly double[] startTimes;
        private readonly double[] endTimes;
        private readonly double[] individualStrains;

        private double individualStrain;
        private double overallStrain;

        private int totalColumnsInMap;
        private int handSplit;

        private int[] anchorCount;
        private int[] trillCount;
        private double[] deltas;

        private int minAnchor = 2;
        private int maxAnchor = 12;

        private int maxTrill = 9;

        public Strain(Mod[] mods, int totalColumns)
            : base(mods)
        {
            startTimes = new double[totalColumns];
            endTimes = new double[totalColumns];
            individualStrains = new double[totalColumns];
            overallStrain = 1;
            totalColumnsInMap = totalColumns;
            anchorCount = new int[totalColumns];
            trillCount = new int[totalColumns];
            deltas = new double[totalColumns];
            handSplit = (int)MathF.Floor(totalColumns / 2);
        }

        protected override double StrainValueOf(DifficultyHitObject current)
        {
            var maniaCurrent = (ManiaDifficultyHitObject)current;
            double startTime = maniaCurrent.StartTime;
            double endTime = maniaCurrent.EndTime;
            int column = maniaCurrent.BaseObject.Column;
            bool isOverlapping = false;

            double closestEndTime = Math.Abs(endTime - startTime); // Lowest value we can assume with the current information
            double holdFactor = 1.0; // Factor to all additional strains in case something else is held
            double holdAddition = 0; // Addition to the current note in case it's a hold and has to be released awkwardly

            for (int i = 0; i < endTimes.Length; ++i)
            {
                // The current note is overlapped if a previous note or end is overlapping the current note body
                isOverlapping |= Precision.DefinitelyBigger(endTimes[i], startTime, 1) && Precision.DefinitelyBigger(endTime, endTimes[i], 1);

                // We give a slight bonus to everything if something is held meanwhile
                if (Precision.DefinitelyBigger(endTimes[i], endTime, 1))
                    holdFactor = 1.25;

                closestEndTime = Math.Min(closestEndTime, Math.Abs(endTime - endTimes[i]));
            }

            // The hold addition is given if there was an overlap, however it is only valid if there are no other note with a similar ending.
            // Releasing multiple notes is just as easy as releasing 1. Nerfs the hold addition by half if the closest release is release_threshold away.
            // holdAddition
            //     ^
            // 1.0 + - - - - - -+-----------
            //     |           /
            // 0.5 + - - - - -/   Sigmoid Curve
            //     |         /|
            // 0.0 +--------+-+---------------> Release Difference / ms
            //         release_threshold
            if (isOverlapping)
                holdAddition = 1 / (1 + Math.Exp(0.5 * (release_threshold - closestEndTime)));

            // Decay and increase individualStrains in own column
            individualStrains[column] = applyDecay(individualStrains[column], startTime - startTimes[column], individual_decay_base);
            individualStrains[column] += 2.0 * holdFactor;

            // For notes at the same time (in a chord), the individualStrain should be the hardest individualStrain out of those columns
            individualStrain = maniaCurrent.DeltaTime <= 1 ? Math.Max(individualStrain, individualStrains[column]) : individualStrains[column];

            // Decay and increase overallStrain
            overallStrain = applyDecay(overallStrain, current.DeltaTime, overall_decay_base);
            overallStrain += (1 + holdAddition) * holdFactor;

            double columnDelta = endTime - endTimes[column];

            // If the difference between columnDeltas for the current note and last note is not greater than 18ms, we count this as an anchor, else break the anchor
            // columnDelta is the difference between the current note's endTime and the last note's endTime
            if (Precision.DefinitelyBigger(deltas[column], columnDelta, 47))
            {
                this.anchorCount[column] = 0;
            }
            else
            {
                this.anchorCount[column]++;
            }

            // we don't give any MORE buff past 12 notes in a row
            double anchorCount = Math.Min(maxAnchor, this.anchorCount[column]);

            // if our anchor is over 2 notes long we apply the buff
            if (anchorCount >= minAnchor)
                individualStrain += 0.47 * (anchorCount * 0.94);

            // we check adjacent columns, starting with the column to the left of this note, if there is one
            for (int adjacentColumn = Math.Max(0, column - 1); adjacentColumn < Math.Min(totalColumnsInMap - 1, column + 1); adjacentColumn++)
            {
                if (adjacentColumn == column)
                    continue;

                // minimum millisecond delta for which we count two notes in adjacent columns as a "trill"
                int minTime = 400;

                // if the startTime for the column we're looking at now is later than the current note, we have the shape of a trill
                // if the difference between the current startTime and the last one falls within our minimum threshold, we're good
                // if there isn't an anchor in the column in the current note, we're also good
                if (startTimes[adjacentColumn] > startTimes[column] && startTime - startTimes[adjacentColumn] < minTime && this.anchorCount[adjacentColumn] < 2)
                {
                    trillCount[column] = Math.Min(maxTrill, trillCount[column] + 1);

                    // we add a buff to repeated trills
                    individualStrain += individualStrains[adjacentColumn] * 0.23 * trillCount[column];
                }
                else
                {
                    trillCount[column] = 0;
                }
            }

            int checkStart = column >= handSplit ? handSplit : 0;
            int checkEnd = column >= handSplit ? totalColumnsInMap - 2 : handSplit - 1;

            int found = 0;

            // check hand for easy one-hand anchors
            // we are assuming half of the playfield is for the left hand and the other half is for the right hand
            // we go through all columns in the hand and check if there's a large anchor occuring in that hand
            // if there is, or if the last note in the column we're checking was 2 seconds ago or more, we increment the number of anchors found
            for (int adjacentColumn = checkStart; adjacentColumn < checkEnd; adjacentColumn++)
            {
                int nextAdjacentColumn = adjacentColumn + 1;

                if ((this.anchorCount[nextAdjacentColumn] > 4 && this.anchorCount[adjacentColumn] > 4) || Math.Abs(startTimes[adjacentColumn] - startTimes[nextAdjacentColumn]) >= 2000)
                {
                    found++;
                }
            }

            // if the number of anchors in the hand found is >= number of columns in the hand, we can apply our nerf
            if (found >= (checkEnd - checkStart))
            {
                individualStrain *= 0.12;
            }

            // Update startTimes and endTimes arrays
            startTimes[column] = startTime;
            endTimes[column] = endTime;
            deltas[column] = columnDelta;

            // By subtracting CurrentStrain, this skill effectively only considers the maximum strain of any one hitobject within each strain section.

            return individualStrain + overallStrain - CurrentStrain;
        }

        protected override double CalculateInitialStrain(double offset, DifficultyHitObject current)
            => applyDecay(individualStrain, offset - current.Previous(0).StartTime, individual_decay_base)
               + applyDecay(overallStrain, offset - current.Previous(0).StartTime, overall_decay_base);

        private double applyDecay(double value, double deltaTime, double decayBase)
            => value * Math.Pow(decayBase, deltaTime / 1000);
    }
}
