﻿using LiveSplit.Options;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LiveSplit.Model.Comparisons
{
    public class AverageSegmentsComparisonGenerator : IComparisonGenerator
    {
        public IRun Run { get; set; }
        public const String ComparisonName = "Average Segments";
        public const String ShortComparisonName = "Average";
        public String Name { get { return ComparisonName; } }

        public AverageSegmentsComparisonGenerator(IRun run)
        {
            Run = run;
        }

        protected TimeSpan CalculateAverage(IEnumerable<TimeSpan> curList)
        {
            var elementCount = curList.Count();
            var weightedList = curList.Select((x, i) => new KeyValuePair<double, TimeSpan>(GetWeight(i, elementCount), x)).ToList();
            weightedList = weightedList.OrderBy(x => x.Value).ToList();
            var totalWeights = weightedList.Aggregate(0.0, (s, x) => (s + x.Key));
            var averageTime = weightedList.Aggregate(0.0, (s, x) => (s + x.Key * x.Value.TotalSeconds)) / totalWeights;
            return TimeSpan.FromSeconds(averageTime);
        }

        protected double GetWeight(int index, int count)
        {
            return Math.Pow(0.9, count - index - 1);
        }

        public void Generate(TimingMethod method)
        {
            var allHistory = new List<List<TimeSpan>>();
            foreach (var segment in Run)
                allHistory.Add(new List<TimeSpan>());
            for (var ind = 1; ind <= Run.RunHistory.Count; ind++)
            {
                var ignoreNextHistory = false;
                foreach (var segment in Run)
                {
                    IIndexedTime history;
                    history = segment.SegmentHistory.FirstOrDefault(x => x.Index == ind);
                    if (history != null)
                    {
                        if (history.Time[method] == null)
                            ignoreNextHistory = true;
                        else if (!ignoreNextHistory)
                        {
                            allHistory[Run.IndexOf(segment)].Add(history.Time[method].Value);
                        }
                        else ignoreNextHistory = false;
                    }
                    else break;
                }
            }
            TimeSpan? totalTime = TimeSpan.Zero;
            for (var ind = 0; ind < Run.Count; ind++)
            {
                var curList = allHistory[ind];
                if (curList.Count == 0)
                    totalTime = null;
                if (totalTime != null)
                    totalTime += CalculateAverage(curList);
                var time = new Time(Run[ind].Comparisons[Name]);
                time[method] = totalTime;
                Run[ind].Comparisons[Name] = time;
            }
        }

        public void Generate(ISettings settings)
        {
            Generate(TimingMethod.RealTime);
            Generate(TimingMethod.GameTime);
        }
    }
}
