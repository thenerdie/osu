// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Utils;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mania.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
//using osu.Framework.Logging;
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

        private double greatHitWindow;

        private double[] deltas;

        //private int maxAnchor = 5;

        public Strain(Mod[] mods, int totalColumns, double greatWindow)
            : base(mods)
        {
            startTimes = new double[totalColumns];
            endTimes = new double[totalColumns];
            individualStrains = new double[totalColumns];
            overallStrain = 1;
            totalColumnsInMap = totalColumns;
            deltas = new double[totalColumns];
            handSplit = (int)MathF.Floor(totalColumns / 2);
            greatHitWindow = greatWindow;
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

            bool applyManipNerf = true;

            for (int i = 0; i < endTimes.Length; ++i)
            {
                // The current note is overlapped if a previous note or end is overlapping the current note body
                isOverlapping |= Precision.DefinitelyBigger(endTimes[i], startTime, 1) && Precision.DefinitelyBigger(endTime, endTimes[i], 1);

                // We give a slight bonus to everything if something is held meanwhile
                if (Precision.DefinitelyBigger(endTimes[i], endTime, 1))
                    holdFactor = 1.25;

                closestEndTime = Math.Min(closestEndTime, Math.Abs(endTime - endTimes[i]));
            }

            //int columnLeft = Math.Max(0, column - 1);
            //int columnRight = Math.Min(totalColumnsInMap - 1, column + 1);

            double columnDelta = startTime - startTimes[column];

            int handLowerBound = column >= handSplit ? handSplit : 0;
            int handUpperBound = column >= handSplit ? totalColumnsInMap - 2 : handSplit - 1;

            int mashableGroups = 0;

            double closestNoteStartTime = startTimes[column];

            // check hand for easy one-hand anchors
            // we are assuming half of the playfield is for the left hand and the other half is for the right hand
            // we go through all columns in the hand and check if there's a large anchor occuring in that hand
            // if there is, or if the last note in the column we're checking was 2 seconds ago or more, we increment the number of anchors found
            for (int adjacentColumn = handLowerBound; adjacentColumn < handUpperBound; adjacentColumn++)
            {
                int nextAdjacentColumn = adjacentColumn + 1;

                if (Math.Abs(startTimes[adjacentColumn] - startTimes[nextAdjacentColumn]) <= greatHitWindow)
                {
                    mashableGroups++;
                }

                closestNoteStartTime = Math.Max(Math.Max(startTimes[adjacentColumn], startTimes[nextAdjacentColumn]), closestNoteStartTime);
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

            // if the number of anchors in the hand found is >= number of columns in the hand, we can apply our nerf
            if (mashableGroups >= (handUpperBound - handLowerBound) && applyManipNerf)
            {
                individualStrains[column] *= 0.95 - Math.Min(maniaCurrent.AnchorCount * 0.05, 0.5);
            }
            else if (startTime - closestNoteStartTime > greatHitWindow * 1.2)
            {
                double maxDenominator = 4.5; // shifts the weighting
                double denominator = -Math.Pow(startTime - closestNoteStartTime, 2) / 9000 + maxDenominator;

                columnDelta = (startTime - closestNoteStartTime + columnDelta) / Math.Max(Math.Min(denominator, maxDenominator), 0); //4.5
            }

            // Decay and increase individualStrains in own column
            individualStrains[column] = applyDecay(individualStrains[column], columnDelta, individual_decay_base);
            individualStrains[column] += 2.0 * holdFactor;

            // For notes at the same time (in a chord), the individualStrain should be the hardest individualStrain out of those columns
            individualStrain = maniaCurrent.DeltaTime <= 1 ? Math.Max(individualStrain, individualStrains[column]) : individualStrains[column];

            // Decay and increase overallStrain
            overallStrain = applyDecay(overallStrain, current.DeltaTime, overall_decay_base);
            overallStrain += (1 + holdAddition) * holdFactor;

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
