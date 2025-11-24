using SemchunkNet.Internal;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace SemchunkNet.Internal
{
	public static class ChunkingConstants
	{
		/// <summary>
		/// Semantically meaningful non-whitespace splitters, ordered from most desirable
		/// to least desirable.
		/// </summary>
		public static readonly string[] NonWhitespaceSemanticSplitters =
		{
	            // Sentence terminators.
	            ".",
			"?",
			"!",
			"*",

	            // Clause separators.
	            ";",
			",",
			"(",
			")",
			"[",
			"]",
			"“",
			"”",
			"‘",
			"’",
			"'",
			"\"",
			"`",

	            // Sentence interrupters.
	            ":",
			"—",
			"…",

	            // Word joiners.
	            "/",
			"\\",
			"–",
			"&",
			"-"
		};

		/// <summary>
		/// Regex-escaped versions of <see cref="NonWhitespaceSemanticSplitters"/>.
		/// </summary>
		public static readonly string[] RegexEscapedNonWhitespaceSemanticSplitters =
			NonWhitespaceSemanticSplitters
				.Select(Regex.Escape)
				.ToArray();
	}

	public static class TextSplitter
	{
		/// <summary>
		/// Split text using the most semantically meaningful splitter possible.
		/// Returns:
		/// - splitter (string),
		/// - splitterIsWhitespace (bool),
		/// - splits (List&lt;string&gt;).
		/// </summary>
		public static (string splitter, bool splitterIsWhitespace, List<string> splits)
			SplitText(string text)
		{
			// Python: splitter_is_whitespace = True
			var splitterIsWhitespace = true;
			string? splitter;

			// 1. Largest sequence of newlines / carriage returns.
			if (text.Contains("\n") || text.Contains("\r"))
			{
				var matches = Regex.Matches(text, @"[\r\n]+");
				splitter = matches
					.Cast<Match>()
					.OrderByDescending(m => m.Value.Length)
					.First()
					.Value;
			}
			// 2. Largest sequence of tabs.
			else if (text.Contains("\t"))
			{
				var matches = Regex.Matches(text, @"\t+");
				splitter = matches
					.Cast<Match>()
					.OrderByDescending(m => m.Value.Length)
					.First()
					.Value;
			}
			// 3. Largest sequence of whitespace, with special single-char case.
			else if (Regex.IsMatch(text, @"\s"))
			{
				var matches = Regex.Matches(text, @"\s+");
				splitter = matches
					.Cast<Match>()
					.OrderByDescending(m => m.Value.Length)
					.First()
					.Value;

				// If the splitter is only a single character, see if we can target
				// whitespace preceded by a semantically meaningful non-whitespace splitter.
				if (splitter.Length == 1)
				{
					foreach (var escapedPreceder in ChunkingConstants.RegexEscapedNonWhitespaceSemanticSplitters)
					{
						// rf'{escaped_preceder}(\s)'
						var pattern = $"{escapedPreceder}(\\s)";
						var match = Regex.Match(text, pattern);
						if (match.Success)
						{
							splitter = match.Groups[1].Value; // the whitespace char
							var escapedSplitter = Regex.Escape(splitter);

							// rf'(?<={escaped_preceder}){escaped_splitter}'
							var splitPattern = $"(?<={escapedPreceder}){escapedSplitter}";
							var parts = Regex.Split(text, splitPattern).ToList();

							return (splitter, splitterIsWhitespace, parts);
						}
					}
				}
			}
			else
			{
				// 4. Semantically meaningful non-whitespace splitter.
				splitter = null;

				foreach (var candidate in ChunkingConstants.NonWhitespaceSemanticSplitters)
				{
					if (text.Contains(candidate))
					{
						splitter = candidate;
						splitterIsWhitespace = false;
						break;
					}
				}

				// No semantically meaningful splitter: return empty splitter
				// and the text as a list of characters.
				if (splitter == null)
				{
					var chars = text.Select(c => c.ToString()).ToList();
					return ("", splitterIsWhitespace, chars);
				}
			}

			// Default: split on the chosen splitter.
			var splits = text
				.Split(new[] { splitter }, StringSplitOptions.None)
				.ToList();

			return (splitter, splitterIsWhitespace, splits);
		}
	}

	public static class SearchUtils
	{
		/// <summary>
		/// Equivalent of Python's bisect_left on a sorted list of integers.
		/// Returns the leftmost index in [low, high) where target could be inserted
		/// to maintain sorted order.
		/// </summary>
		public static int BisectLeft(IList<int> sorted, double target, int low, int high)
		{
			while (low < high)
			{
				var mid = (low + high) / 2;

				if (sorted[mid] < target)
					low = mid + 1;
				else
					high = mid;
			}
			return low;
		}
	}


	public static class MergeUtils
	{
		/// <summary>
		/// Merge splits until a chunk size is reached, returning:
		/// - the index of the last split included in the merged chunk
		/// - and the merged chunk itself.
		/// </summary>
		public static (int endIndex, string chunk) MergeSplits(
			IList<string> splits,
			IList<int> cumLens,
			int chunkSize,
			string splitter,
			Func<string, int> tokenCounter,
			int start,
			int high)
		{
			double average = 0.2;
			int low = start;

			int offset = cumLens[start];
			double target = offset + (chunkSize * average);

			while (low < high)
			{
				int i = SearchUtils.BisectLeft(cumLens, target, low, high);
				int midpoint = Math.Min(i, high - 1);

				// Python: splitter.join(splits[start:midpoint])
				string segment = JoinRange(splits, splitter, start, midpoint);
				int tokens = tokenCounter(segment);

				int localCum = cumLens[midpoint] - offset;

				if (localCum != 0 && tokens > 0)
				{
					average = (double)localCum / tokens;
					target = offset + (chunkSize * average);
				}

				if (tokens > chunkSize)
				{
					high = midpoint;
				}
				else
				{
					low = midpoint + 1;
				}
			}

			int end = low - 1;

			// Python: splitter.join(splits[start:end])
			string chunk = JoinRange(splits, splitter, start, end);

			return (end, chunk);
		}

		/// <summary>
		/// Join splits[start:endExclusive] with splitter (endExclusive is exclusive).
		/// Mirrors Python's list slicing semantics.
		/// </summary>
		private static string JoinRange(
			IList<string> splits,
			string splitter,
			int start,
			int endExclusive)
		{
			int count = endExclusive - start;
			if (count <= 0)
				return string.Empty;

			// Avoid LINQ allocations; we're on hot path.
			var sb = new StringBuilder();

			for (int i = 0; i < count; i++)
			{
				if (i > 0)
					sb.Append(splitter);

				sb.Append(splits[start + i]);
			}

			return sb.ToString();
		}
	}

	public readonly struct ChunkResult
	{
		public IReadOnlyList<string> Chunks { get; }
		public IReadOnlyList<(int Start, int End)> Offsets { get; }

		public ChunkResult(List<string> chunks, List<(int, int)> offsets)
		{
			Chunks = chunks;
			Offsets = offsets;
		}
	}

	public static class ChunkerCore
	{
		/// <summary>
		/// Split a text into semantically meaningful chunks of a specified token size.
		/// This mirrors the behaviour of semchunk.chunk.
		/// </summary>
		public static ChunkResult Chunk(
			string text,
			int chunkSize,
			Func<string, int> tokenCounter,
			bool memoize = true,
			bool returnOffsets = false,
			double? overlap = null,          // null → no overlap; <1 = fraction; >=1 = tokens
			int? cacheMaxSize = null,        // ignored for now; placeholder for parity
			int recursionDepth = 0,
			int startOffset = 0)
		{
			// Rename variables for clarity.
			bool isFirstCall = recursionDepth == 0;
			int localChunkSize = chunkSize;
			int unoverlappedChunkSize = chunkSize;
			bool hasOverlap = false;

			// Memoize + overlap logic only on first call.
			if (isFirstCall)
			{
				if (memoize)
				{
					tokenCounter = ChunkerCoreMemoization.MemoizeTokenCounter(tokenCounter, cacheMaxSize);
				}

				if (overlap.HasValue && overlap.Value != 0.0)
				{
					double raw = overlap.Value;

					// Make relative overlaps absolute and floor, prevent overlap >= chunkSize
					double overlapTokens =
						raw < 1.0
							? Math.Floor(chunkSize * raw)
							: Math.Min(raw, chunkSize - 1);

					if (overlapTokens > 0)
					{
						hasOverlap = true;
						int overlapInt = (int)overlapTokens;
						unoverlappedChunkSize = chunkSize - overlapInt;
						localChunkSize = Math.Min(overlapInt, unoverlappedChunkSize);
					}
				}
			}

			// Split the text using the most semantically meaningful splitter possible.
			var (splitter, splitterIsWhitespace, splitsList) = TextSplitter.SplitText(text);
			var splits = (IList<string>)splitsList;

			var offsets = new List<(int Start, int End)>();
			int splitterLen = splitter.Length;

			// split_lens
			var splitLens = new List<int>(splits.Count);
			for (int i = 0; i < splits.Count; i++)
				splitLens.Add(splits[i].Length);

			// cum_lens = accumulate(split_lens, initial=0)
			var cumLens = new List<int>(splits.Count + 1) { 0 };
			int running = 0;
			for (int i = 0; i < splitLens.Count; i++)
			{
				running += splitLens[i];
				cumLens.Add(running);
			}

			// split_starts: start index of each split in original text, plus one past-the-end entry
			var splitStarts = new List<int>(splits.Count + 1);
			int currentStart = startOffset;

			// First element corresponds to the start before any splits.
			splitStarts.Add(currentStart);

			for (int i = 0; i < splits.Count; i++)
			{
				// For split i, its start is splitStarts[i] (already added).
				// Now advance to the start AFTER this split + splitter and store as next.
				currentStart += splitLens[i] + splitterLen;
				splitStarts.Add(currentStart);
			}

			int numSplitsPlusOne = splits.Count + 1;

			var chunks = new List<string>();
			var skips = new HashSet<int>();

			// Iterate through the splits.
			for (int i = 0; i < splits.Count; i++)
			{
				string split = splits[i];
				int splitStart = splitStarts[i];

				// Skip the split if it has already been added to a chunk.
				if (skips.Contains(i))
					continue;

				// If the split is over the chunk size, recursively chunk it.
				if (tokenCounter(split) > localChunkSize)
				{
					var nested = Chunk(
						text: split,
						chunkSize: localChunkSize,
						tokenCounter: tokenCounter,
						memoize: false,              // Python: memoization only at top level
						returnOffsets: true,
						overlap: null,               // Python: overlap only at top level
						cacheMaxSize: null,
						recursionDepth: recursionDepth + 1,
						startOffset: splitStart);

					chunks.AddRange(nested.Chunks);
					offsets.AddRange(nested.Offsets);
				}
				else
				{
					// Merge the split with subsequent splits until the chunk size is reached.
					var (finalSplitIndex, newChunk) = MergeUtils.MergeSplits(
						splits: splits,
						cumLens: cumLens,
						chunkSize: localChunkSize,
						splitter: splitter,
						tokenCounter: tokenCounter,
						start: i,
						high: numSplitsPlusOne);

					// Mark any splits included in the new chunk for exclusion.
					for (int j = i + 1; j < finalSplitIndex; j++)
						skips.Add(j);

					// Add the chunk.
					chunks.Add(newChunk);

					// Add the chunk's offsets.
					int splitEnd = splitStarts[finalSplitIndex] - splitterLen;
					offsets.Add((splitStart, splitEnd));
				}

				// If the splitter is not whitespace and this is not effectively the last split,
				// attach splitter to last chunk if it fits, otherwise make splitter its own chunk.
				bool isLastSplit = (i == splits.Count - 1);
				bool allFollowingSkipped = true;
				if (!isLastSplit)
				{
					for (int j = i + 1; j < splits.Count; j++)
					{
						if (!skips.Contains(j))
						{
							allFollowingSkipped = false;
							break;
						}
					}
				}

				if (!splitterIsWhitespace && !(isLastSplit || allFollowingSkipped))
				{
					// Append splitter to previous chunk if it doesn't exceed localChunkSize.
					string lastChunk = chunks[chunks.Count - 1];
					string lastChunkWithSplitter = lastChunk + splitter;

					if (tokenCounter(lastChunkWithSplitter) <= localChunkSize)
					{
						chunks[chunks.Count - 1] = lastChunkWithSplitter;
						var (start, end) = offsets[offsets.Count - 1];
						offsets[offsets.Count - 1] = (start, end + splitterLen);
					}
					else
					{
						int start = offsets.Count > 0 ? offsets[offsets.Count - 1].End : splitStart;
						chunks.Add(splitter);
						offsets.Add((start, start + splitterLen));
					}
				}
			}

			// If this is the first call, cleanse + overlap + maybe drop offsets.
			if (isFirstCall)
			{
				// Remove empty chunks and whitespace-only chunks.
				var filteredChunks = new List<string>();
				var filteredOffsets = new List<(int, int)>();

				for (int i = 0; i < chunks.Count; i++)
				{
					var chunk = chunks[i];
					if (!string.IsNullOrEmpty(chunk) && !string.IsNullOrWhiteSpace(chunk))
					{
						filteredChunks.Add(chunk);
						filteredOffsets.Add(offsets[i]);
					}
				}

				chunks = filteredChunks;
				offsets = filteredOffsets;

				// Overlap chunks if desired.
				if (hasOverlap && chunks.Count > 0)
				{
					int subchunkSize = localChunkSize;
					var subchunks = chunks;
					var suboffsets = offsets;
					int numSubchunks = subchunks.Count;

					// Merge the subchunks into overlapping chunks.
					int subchunksPerChunk = (int)Math.Floor((double)chunkSize / subchunkSize);
					int subchunkStride = (int)Math.Floor((double)unoverlappedChunkSize / subchunkSize);

					var overlappedOffsets = new List<(int, int)>();
					var overlappedChunks = new List<string>();

					int maxI = Math.Max(
						1,
						(int)Math.Ceiling((double)(numSubchunks - subchunksPerChunk) / subchunkStride) + 1);

					for (int i = 0; i < maxI; i++)
					{
						int startIndex = i * subchunkStride;
						if (startIndex >= numSubchunks)
							break;

						int endIndex = Math.Min(startIndex + subchunksPerChunk, numSubchunks) - 1;
						if (endIndex < startIndex)
							break;

						int start = suboffsets[startIndex].Start;
						int end = suboffsets[endIndex].End;
						overlappedOffsets.Add((start, end));
						overlappedChunks.Add(text.Substring(start, end - start));
					}

					offsets = overlappedOffsets;
					chunks = overlappedChunks;
				}

				// If caller doesn't want offsets, we can ignore them.
				if (!returnOffsets)
				{
					return new ChunkResult(chunks, new List<(int, int)>()); // Offsets unused
				}

				return new ChunkResult(chunks, offsets);
			}

			// Recursive call: always return chunks and offsets.
			return new ChunkResult(chunks, offsets);
		}
	}

	internal static class ChunkerCoreMemoization
	{
		/// <summary>
		/// Map of token counters to their memoized versions.
		/// </summary>
		internal static readonly ConcurrentDictionary<Func<string, int>, Func<string, int>>
			MemoizedTokenCounters = new();

		public static Func<string, int> MemoizeTokenCounter(
			Func<string, int> tokenCounter,
			int? cacheMaxSize)
		{
			if (MemoizedTokenCounters.TryGetValue(tokenCounter, out var existing))
				return existing;

			// Unbounded cache for now; cacheMaxSize ignored (same as Python's default None).
			var cache = new Dictionary<string, int>();

			int Wrapped(string s)
			{
				if (cache.TryGetValue(s, out var v))
					return v;

				var count = tokenCounter(s);
				cache[s] = count;
				return count;
			}

			var wrapped = (Func<string, int>)Wrapped;
			MemoizedTokenCounters[tokenCounter] = wrapped;
			return wrapped;
		}
	}
}


namespace SemchunkNet
{
	/// <summary>
	/// Port of semchunk.Chunker.
	/// Holds chunk size and token counter and exposes methods to chunk text(s).
	/// </summary>
	public sealed class Chunker
	{
		/// <summary>
		/// Maximum number of tokens per chunk.
		/// </summary>
		public int ChunkSize { get; }

		/// <summary>
		/// Function that returns the number of tokens in a given string.
		/// </summary>
		public Func<string, int> TokenCounter { get; }

		public Chunker(int chunkSize, Func<string, int> tokenCounter)
		{
			if (chunkSize <= 0)
				throw new ArgumentOutOfRangeException(nameof(chunkSize), "chunkSize must be positive.");
			ChunkSize = chunkSize;

			TokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
		}

		// --- internal analogue of _make_chunk_function ------------------------

		private Func<string, ChunkResult> MakeChunkFunction(
			bool returnOffsets,
			double? overlap)
		{
			// This mirrors the inner _chunk() closure in Python:
			// memoize=False (top-level memoization is handled elsewhere, e.g. in chunkerify).
			return text => ChunkerCore.Chunk(
				text: text,
				chunkSize: ChunkSize,
				tokenCounter: TokenCounter,
				memoize: false,
				returnOffsets: returnOffsets,
				overlap: overlap);
		}

		// --- Single text versions --------------------------------------------

		/// <summary>
		/// Chunk a single text. Offsets are not returned.
		/// </summary>
		public IReadOnlyList<string> Chunk(
			string text,
			double? overlap = null)
		{
			if (text == null)
				throw new ArgumentNullException(nameof(text));

			var chunkFunction = MakeChunkFunction(returnOffsets: false, overlap: overlap);
			var result = chunkFunction(text);
			return result.Chunks;
		}

		/// <summary>
		/// Chunk a single text and also return character offsets (start, end) in the original text.
		/// </summary>
		public IReadOnlyList<string> Chunk(
			string text,
			out IReadOnlyList<(int Start, int End)> offsets,
			double? overlap = null)
		{
			if (text == null)
				throw new ArgumentNullException(nameof(text));

			var chunkFunction = MakeChunkFunction(returnOffsets: true, overlap: overlap);
			var result = chunkFunction(text);
			offsets = result.Offsets;
			return result.Chunks;
		}

		// --- Multiple texts versions (Python: text_or_texts is Sequence[str]) -

		/// <summary>
		/// Chunk multiple texts. Returns one chunk list per input text.
		/// </summary>
		public IReadOnlyList<IReadOnlyList<string>> ChunkMany(
			IReadOnlyList<string> texts,
			double? overlap = null)
		{
			if (texts == null)
				throw new ArgumentNullException(nameof(texts));

			var chunkFunction = MakeChunkFunction(returnOffsets: false, overlap: overlap);

			var allChunks = new List<IReadOnlyList<string>>(texts.Count);
			for (int i = 0; i < texts.Count; i++)
			{
				var text = texts[i] ?? string.Empty;
				var result = chunkFunction(text);
				allChunks.Add(result.Chunks);
			}

			return allChunks;
		}

		/// <summary>
		/// Chunk multiple texts and also return offsets for each chunk per text.
		/// </summary>
		public IReadOnlyList<IReadOnlyList<string>> ChunkMany(
			IReadOnlyList<string> texts,
			out IReadOnlyList<IReadOnlyList<(int Start, int End)>> allOffsets,
			double? overlap = null)
		{
			if (texts == null)
				throw new ArgumentNullException(nameof(texts));

			var chunkFunction = MakeChunkFunction(returnOffsets: true, overlap: overlap);

			var allChunks = new List<IReadOnlyList<string>>(texts.Count);
			var allOffsetsLocal = new List<IReadOnlyList<(int Start, int End)>>(texts.Count);

			for (int i = 0; i < texts.Count; i++)
			{
				var text = texts[i] ?? string.Empty;
				var result = chunkFunction(text);
				allChunks.Add(result.Chunks);
				allOffsetsLocal.Add(result.Offsets);
			}

			allOffsets = allOffsetsLocal;
			return allChunks;
		}
	}

	public static class ChunkerFactory
	{
		/// <summary>
		/// Construct a Chunker from a token counter.
		/// </summary>
		public static Chunker Create(
			Func<string, int> tokenCounter,
			int chunkSize,
			int? maxTokenChars = null,
			bool memoize = true,
			int? cacheMaxSize = null)
		{
			if (tokenCounter == null)
				throw new ArgumentNullException(nameof(tokenCounter));
			if (chunkSize <= 0)
				throw new ArgumentOutOfRangeException(nameof(chunkSize));

			// If we know the number of characters in the longest token,
			// construct a faster token counter that avoids tokenizing very long texts.
			var effectiveTokenCounter = tokenCounter;
			if (maxTokenChars.HasValue)
			{
				int maxChars = maxTokenChars.Value - 1;
				var original = effectiveTokenCounter;

				int Faster(string text)
				{
					int heuristic = chunkSize * 6;

					if (text.Length > heuristic)
					{
						var prefixLen = heuristic + maxChars;
						if (prefixLen > text.Length)
							prefixLen = text.Length;

						var prefix = text.Substring(0, prefixLen);
						if (original(prefix) > chunkSize)
							return chunkSize + 1;
					}

					return original(text);
				}

				effectiveTokenCounter = Faster;
			}

			// Memoize token counter at top level if requested.
			if (memoize)
			{
				effectiveTokenCounter = ChunkerCoreMemoization.MemoizeTokenCounter(
					effectiveTokenCounter,
					cacheMaxSize);
			}

			return new Chunker(chunkSize, effectiveTokenCounter);
		}

		/// <summary>
		/// Construct a Chunker from a tokenizer, inferring chunk size when not specified.
		/// </summary>
		public static Chunker Create(
			ITokenizer tokenizer,
			int? chunkSize = null,
			int? maxTokenChars = null,
			bool memoize = true,
			int? cacheMaxSize = null)
		{
			if (tokenizer == null)
				throw new ArgumentNullException(nameof(tokenizer));

			int resolvedChunkSize = chunkSize ?? InferChunkSize(tokenizer);

			// Build token counter from tokenizer.Encode
			int TokenCounter(string text) => tokenizer.Encode(text).Length;

			// TODO: if your tokenizer exposes vocab or longest token, you can set maxTokenChars here.
			return Create(TokenCounter, resolvedChunkSize, maxTokenChars, memoize, cacheMaxSize);
		}

		private static int InferChunkSize(ITokenizer tokenizer)
		{
			var max = tokenizer.ModelMaxLength;
			if (max <= 0)
				throw new InvalidOperationException(
					"ModelMaxLength must be a positive integer to infer chunk size.");

			// Python tries to reduce by the number of special tokens for empty string.
			// If you have that concept, subtract it here; otherwise just return ModelMaxLength.
			return max;
		}
	}
}
