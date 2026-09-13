using System;
using PushStars.Core;

namespace PushStars.Editor
{
    public static class TrainingSettingsRegression
    {
        public static void Run()
        {
            Require(new TrainingPlan(3, 60).EstimatedSeconds == 300, "Default workout must take five minutes.");
            Require(new TrainingPlan(1, 90).EstimatedSeconds == 60, "No rest after the last set.");
            Require(new TrainingPlan(1, -1).EstimatedSeconds == 60, "One set needs no manual rest.");
            Require(new TrainingPlan(3, -1).EstimatedSeconds == null, "Manual rest has no finite estimate.");
            Require(new TrainingPlan(-20, 15).Sets == 1 && new TrainingPlan(99, 15).Sets == 10, "Corrupt set bounds are clamped.");
            Require(new TrainingPlan(3, 15).RestSeconds == 60, "Unsupported rest falls back to sixty seconds.");
            var timed = new TrainingProgress(new TrainingPlan(3, 30));
            timed.CompleteSet();
            timed.CompleteSet();
            Require(timed.CompletedSets == 1 && timed.IsResting, "A duplicate completion during rest cannot skip a set.");
            Require(!timed.TickRest(29) && timed.TickRest(1), "Timed rest resumes only at the deadline.");
            timed.CompleteSet(); Require(timed.Continue(), "Rest can be ended manually.");
            timed.CompleteSet(); timed.CompleteSet();
            Require(timed.IsComplete && !timed.IsResting && timed.CompletedSets == 3, "Workout ends exactly after the requested set count.");
            var manual = new TrainingProgress(new TrainingPlan(2, -1));
            manual.CompleteSet();
            Require(!manual.TickRest(100000) && manual.IsResting, "Infinite rest must wait for input.");
            Require(manual.Continue() && !manual.Continue(), "Manual rest resumes exactly once.");
            manual.CompleteSet(); Require(manual.IsComplete && !manual.IsResting, "Final set skips rest.");
            Require(manual.AddSet() && !manual.IsComplete && manual.CurrentSet == 3, "One more set extends completed workout without resetting totals.");
            manual.CompleteSet(); Require(manual.CompletedSets == 3, "Extra set completes exactly once.");
            var extended = new TrainingProgress(new TrainingPlan(2, 30));
            extended.CompleteSet(); extended.AddRest(15);
            Require(!extended.TickRest(44) && extended.TickRest(1), "Adding fifteen seconds moves the rest deadline.");
            var capped = new TrainingProgress(new TrainingPlan(10, 30));
            for (int i = 0; i < 10; i++) { capped.CompleteSet(); capped.Continue(); }
            Require(!capped.AddSet(), "Extra sets respect the workout limit.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}
