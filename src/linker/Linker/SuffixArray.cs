// https://pastebin.com/sHaHPq2v

using System;
using System.Collections.Generic;
using System.Linq;

namespace SuffixArray
{
	public class SuffixArray<T>
	{
		private readonly Dictionary<T, int> charRepresentation;
		private readonly List<int> stringsAsIntegers;
		public int[] SortedCyclicShifts;
		public int[] LongestCommonPrefix;

		public SuffixArray(List<T> s)
		{
			charRepresentation = new Dictionary<T, int>();
			stringsAsIntegers = new List<int>();

			for (int i = 0; i < s.Count; i++)
			{
				if (!charRepresentation.TryGetValue(s[i], out int strRepresentation))
				{
					strRepresentation = charRepresentation.Count + 1;
					charRepresentation[s[i]] = strRepresentation;
				}

				stringsAsIntegers.Add(strRepresentation);
			}

			Build();
			BuildLCP();
		}

		public List<T> GetLongestRepeatedSubstring()
		{
			int longestPrefixSize = LongestCommonPrefix.Max();
			int longestPrefix = Array.IndexOf(LongestCommonPrefix, longestPrefixSize);
			// Now we go back from our integer representation to a string...
			// The mapping is bijective so this is fine.
			var reverseDict = charRepresentation.ToDictionary(k => k.Value, v => v.Key);
			List<T> longestRepeatedSubstring = new List<T> ();
			for (int i = SortedCyclicShifts[longestPrefix];
				i < SortedCyclicShifts[longestPrefix] + longestPrefixSize; i++)
				longestRepeatedSubstring.Add(reverseDict[stringsAsIntegers[i]]);

			return longestRepeatedSubstring;
		}

		/// <summary>
		/// We can simplify this by defining our comparison function and calling
		/// Array.Sort on our array of cyclic shifts.
		/// </summary>
		void Build()
		{
			stringsAsIntegers.Add(0); // This is '$'
			int alphabetSize = charRepresentation.Count + 1; // Plus one because of '$'
			int stringSize = stringsAsIntegers.Count; // We will sort the cyclic shifts
			int[] elementCount = new int[Math.Max (alphabetSize, stringSize)];
			int[] permutations = new int[stringSize];
			int[] eqClasses = new int[stringSize];

			for (int i = 0; i < stringSize; i++)
				elementCount[stringsAsIntegers[i]]++;

			for (int i = 1; i < alphabetSize; i++)
				elementCount[i] += elementCount[i - 1];

			for (int i = 0; i < stringSize; i++)
				permutations[--elementCount[stringsAsIntegers[i]]] = i;

			eqClasses[permutations[0]] = 0;
			int classes = 1;
			for (int i = 1; i < stringSize; i++)
			{
				if (stringsAsIntegers[permutations[i]] != stringsAsIntegers[permutations[i - 1]])
					classes++;

				eqClasses[permutations[i]] = classes - 1;
			}

			int[] sndPermutation = new int[stringSize];
			int[] sndEqClasses = new int[stringSize];
			for (int j = 0; (1 << j) < stringSize; ++j)
			{
				for (int i = 0; i < stringSize; i++)
				{
					sndPermutation[i] = permutations[i] - (1 << j);
					if (sndPermutation[i] < 0)
						sndPermutation[i] += stringSize;
				}

				Array.Fill(elementCount, 0, 0, classes);
				for (int i = 0; i < stringSize; i++)
					elementCount[eqClasses[sndPermutation[i]]]++;

				for (int i = 1; i < classes; i++)
					elementCount[i] += elementCount[i - 1];

				for (int i = stringSize - 1; i >= 0; i--)
					permutations[--elementCount[eqClasses[sndPermutation[i]]]] = sndPermutation[i];

				sndEqClasses[permutations[0]] = 0;
				classes = 1;
				for (int i = 1; i < stringSize; i++)
				{
					var currentPair = (eqClasses[permutations[i]], eqClasses[(permutations[i] + (1 << j)) % stringSize]);
					var previousPair = (eqClasses[permutations[i-1]], eqClasses[(permutations[i-1] + (1 << j)) % stringSize]);
					if (currentPair != previousPair)
						classes++;

					sndEqClasses[permutations[i]] = classes - 1;
				}

				var holdMyArray = eqClasses;
				eqClasses = sndEqClasses;
				sndEqClasses = holdMyArray;
			}

			SortedCyclicShifts = permutations.Skip(1).ToArray();
			// We are no longer sorting, so remove '$'
			stringsAsIntegers.RemoveAt(stringSize - 1);
		}

		/// <summary>
		/// We reduce LCP to RMQ and use Kasai's algorithm to construct
		/// LCP in O(n).
		/// </summary>
		void BuildLCP()
		{
			int size = stringsAsIntegers.Count;
			int[] rank = new int[size];
			for (int i = 0; i < size; i++)
				rank[SortedCyclicShifts[i]] = i;

			LongestCommonPrefix = new int[size - 1];
			for (int i = 0, k = 0; i < size; i++)
			{
				if (rank[i] == size - 1)
				{
					k = 0;
					continue;
				}

				int j = SortedCyclicShifts[rank[i] + 1];
				while (i + k < size &&
					j + k < size &&
					stringsAsIntegers[i + k] == stringsAsIntegers[j + k])
					k++;

				LongestCommonPrefix[rank[i]] = k;
				if (k > 0)
					k--;
			}
		}
	}
}
