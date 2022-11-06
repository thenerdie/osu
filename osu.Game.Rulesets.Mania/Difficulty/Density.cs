// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// Density.GetDensityValue(beatmap, rate) / ((columnCount + 4) * Math.Pow(0.85, columnCount))

//foreach (var mod in mods)
//{
//    switch (mod.Acronym)
//    {
//        case "DT":
//            rate = 1.5;
//            break;
//        case "NC":
//            rate = 1.5;
//            break;
//        case "HT":
//            rate = 0.75;
//            break;
//    }
//}

using System.Collections.Generic;
using osu.Game.Rulesets.Objects;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.Mania.Difficulty
{
    public static class Density
    {
        public static double GetDensityValue(IBeatmap beatmap, double rate = 1)
        {
            IReadOnlyList<HitObject> hitObjects = beatmap.HitObjects;

            List<double> npsValues = new List<double>();
            List<HitObject> thisSecond = new List<HitObject>();

            double thisSecondStart = beatmap.HitObjects[0].StartTime;

            foreach (var hitObject in hitObjects)
            {
                double startTime = hitObject.StartTime / rate;

                if (startTime - thisSecondStart <= 1000)
                {
                    thisSecond.Add(hitObject);
                }
                else
                {
                    double difficultyValue = thisSecond.Count;

                    npsValues.Add(difficultyValue);
                    thisSecond.Clear();
                    thisSecond.Add(hitObject);

                    thisSecondStart = startTime;
                }
            }

            npsValues.Sort((a, b) => -a.CompareTo(b));

            if (npsValues.Count > 50)
                npsValues.RemoveRange(35, npsValues.Count - 35);

            double avg = 0;

            foreach (int nps in npsValues)
            {
                avg += nps;
            }

            return avg / npsValues.Count;
        }
    }
}
