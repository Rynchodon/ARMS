using System;
using System.Collections.Generic;
using VRage.Library.Utils;

namespace Rynchodon
{
	/// <summary>
	/// Uses high-resolution timer and time span classes (MyGameTimer and MyTimeSpan) to time the execution of an action.
	/// </summary>
	public static class TimeAction
	{
		/// <summary>
		/// Results from timing an Action. This class is lazy-initialized.
		/// </summary>
		/// <remarks>
		/// Mean does not allways produce sane results.
		/// </remarks>
		public class Results
		{
			public int Count { get; private set; }
			/// <summary>
			/// The time each execution took, from first to last.
			/// </summary>
			public ReadOnlyList<MyTimeSpan> UnsortedResults { get; private set; }
			private Lazy<ReadOnlyList<MyTimeSpan>> Lazy_SortedResults;
			/// <summary>
			/// The time each execution took, from shortest to longest.
			/// </summary>
			public ReadOnlyList<MyTimeSpan> SortedResults { get { return Lazy_SortedResults.Value; } }

			public MyTimeSpan Min { get { return SortedResults[0]; } }
			public MyTimeSpan Max { get { return SortedResults[Count - 1]; } }

			private Lazy<MyTimeSpan> Lazy_Total, Lazy_Mean, Lazy_Median, Lazy_FirstQuartile, Lazy_ThirdQuartile;
			public MyTimeSpan Total { get { return Lazy_Total.Value; } }
			public MyTimeSpan Mean { get { return Lazy_Mean.Value; } }
			public MyTimeSpan Median { get { return Lazy_Median.Value; } }
			public MyTimeSpan FirstQuartile { get { return Lazy_FirstQuartile.Value; } }
			public MyTimeSpan ThirdQuartile { get { return Lazy_ThirdQuartile.Value; } }

			/// <summary>
			/// Create Results from a List of execution times.
			/// </summary>
			/// <param name="executionTimes">Execution times. Results does not create a copy, so the list should not be changed.</param>
			internal Results(ListSnapshots<MyTimeSpan> executionTimes)
			{
				UnsortedResults = executionTimes.immutable();
				Count = UnsortedResults.Count;

				Lazy_SortedResults = new Lazy<ReadOnlyList<MyTimeSpan>>(() =>
				{
					executionTimes.mutable().Sort((MyTimeSpan first, MyTimeSpan second) => { return Math.Sign((first - second).Ticks); });
					return new ReadOnlyList<MyTimeSpan>(executionTimes.immutable());
				});

				Lazy_Total = new Lazy<MyTimeSpan>(() =>
				{
					MyTimeSpan sum = new MyTimeSpan(0);
					foreach (MyTimeSpan result in UnsortedResults)
						sum += result;
					return sum;
				});

				Lazy_Mean = new Lazy<MyTimeSpan>(() => { return new MyTimeSpan(Total.Ticks / Count); });
				Lazy_Median = new Lazy<MyTimeSpan>(() => SortedResults_Interpolate((decimal)Count / 2 - 0.5m));
				Lazy_FirstQuartile = new Lazy<MyTimeSpan>(() => SortedResults_Interpolate((decimal)Count / 4 - 0.5m));
				Lazy_ThirdQuartile = new Lazy<MyTimeSpan>(() => SortedResults_Interpolate((decimal)Count * 3 / 4 - 0.5m));
			}

			/// <summary>
			/// Interpolate a value between two consecutive indecies in SortedResults.
			/// </summary>
			/// <remarks>
			///		<example>
			///		SortedResults_Interpolate(1.25) would return a value of SortedResults[1] * .75 + SortedResults[2] * .25
			///		</example>
			/// </remarks>
			/// <param name="index">Representation of the position of the interpolated value.</param>
			/// <returns></returns>
			public MyTimeSpan SortedResults_Interpolate(decimal index)
			{
				int lowerIndex = (int)index;
				int upperIndex = lowerIndex + 1;
				decimal upperWeight = index - lowerIndex;
				decimal lowerWeight = 1 - upperWeight;

				long ticks = (long)(SortedResults[lowerIndex].Ticks * lowerWeight + SortedResults[upperIndex].Ticks * upperWeight);
				return new MyTimeSpan(ticks);
			}

			/// <summary>
			/// Five number summary with pretty seconds.
			/// </summary>
			/// <returns>Five number summary</returns>
			public string Pretty_FiveNumbers()
			{
				if (Count == 1)
					return "One Action: " + Min.ToPrettySeconds();
				if (Count == 2)
					return "Two Actions: " + Min.ToPrettySeconds() + ", " + Max.ToPrettySeconds();
				return "Five number summary: " + Min.ToPrettySeconds() + ", " + FirstQuartile.ToPrettySeconds() + ", " + Median.ToPrettySeconds() + ", " + ThirdQuartile.ToPrettySeconds() + ", " + Max.ToPrettySeconds();
			}

			/// <summary>
			/// With Mean and Median as pretty seconds.
			/// </summary>
			/// <returns>Mean and Median</returns>
			public string Pretty_Average()
			{ return "Mean = " + Mean.ToPrettySeconds() + ", Median = " + Lazy_Median; }
		}

		/// <summary>
		/// Time an Action
		/// </summary>
		/// <param name="action">Action to perform</param>
		/// <param name="iterations">Number of iterations of action</param>
		/// <param name="ignoreFirst">Perform an extra invokation first, that will not be timed.</param>
		/// <returns>Results of timing</returns>
		/// <exception cref="ArgumentNullException">If action == null</exception>
		/// <exception cref="ArgumentOutOfRangeException">If iterarions &lt; 1</exception>
		public static Results Time(Action action, int iterations = 1, bool extraFirst = false)
		{
			VRage.Exceptions.ThrowIf<ArgumentNullException>(action == null, "action");
			VRage.Exceptions.ThrowIf<ArgumentOutOfRangeException>(iterations < 1, "iterations < 1");

			if (extraFirst)
				action.Invoke();
			ListSnapshots<MyTimeSpan> unsortedResults = new ListSnapshots<MyTimeSpan>(iterations);
			ReadOnlyList<MyTimeSpan> mutable = unsortedResults.mutable();
			for (int i = 0; i < iterations; i++)
			{
				MyGameTimer actionTimer = new MyGameTimer();
				action.Invoke();
				mutable.Add(actionTimer.Elapsed);
			}
			return new Results(unsortedResults);
		}

		public static double GetMean(Action action, int iterations = 1, bool extraFirst = false)
		{
			VRage.Exceptions.ThrowIf<ArgumentNullException>(action == null, "action");
			VRage.Exceptions.ThrowIf<ArgumentOutOfRangeException>(iterations < 1, "iterations < 1");

			if (extraFirst)
				action.Invoke();
			MyGameTimer actionTimer = new MyGameTimer();
			for (int i = 0; i < iterations; i++)
				action.Invoke();
			return actionTimer.Elapsed.Seconds / iterations;
		}

	}
}
