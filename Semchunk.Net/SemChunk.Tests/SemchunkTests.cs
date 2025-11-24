using SemchunkNet.Internal;

namespace SemchunkNet.Tests;

public class SemchunkTests
{
	// ---- Deterministic test data ---------------------------------------

	private const string DeterministicInput = "ThisIs\tATest.";
	private const int DeterministicChunkSize = 4;

	// Equivalent to DETERMINISTIC_TEST_OUTPUT_CHUNKS["char"]
	private static readonly IReadOnlyList<string> DeterministicCharChunks =
		new[] { "This", "Is", "ATes", "t." };

	// Equivalent to DETERMINISTIC_TEST_OUTPUT_OFFSETS["char"]
	private static readonly IReadOnlyList<(int Start, int End)> DeterministicCharOffsets =
		new (int, int)[] { (0, 4), (4, 6), (7, 11), (11, 13) };

	// Simple "char" token counter: one char == one token.
	private static readonly Func<string, int> CharTokenCounter = s => s?.Length ?? 0;

	// --------------------------------------------------------------------
	// 1. Basic invariant: chunks respect max token size & map back via offsets.
	// --------------------------------------------------------------------

	[Fact]
	public void Chunker_Respects_MaxSize_And_Offsets_ForCharCounter()
	{
		// Arrange
		var chunkSizes = new[] { 10, 512 };
		const bool testOffsets = true;
		var textSamples = new[]
		{
			"Short sample.",
			"This is a longer sample text that should exercise the chunker logic " +
			"over multiple sentences, punctuation, and whitespace.\nSecond line here."
		};

		foreach (var baseChunkSize in chunkSizes)
		{
			var chunkSize = baseChunkSize;

			// Python adjusts chunk size if tokenizer adds special tokens for empty string.
			// For char counter, tokenCounter("") == 0, so no adjustment.
			if (CharTokenCounter(string.Empty) > 0)
			{
				chunkSize += CharTokenCounter(string.Empty) + 1;
			}

			var chunker = ChunkerFactory.Create(CharTokenCounter, chunkSize);

			foreach (var sample in textSamples)
			{
				// Act: chunks without offsets
				var chunks = chunker.Chunk(sample);

				// Assert: each chunk within max size and non-empty/non-whitespace
				foreach (var chunk in chunks)
				{
					Assert.True(CharTokenCounter(chunk) <= chunkSize);
					Assert.False(string.IsNullOrEmpty(chunk));
					Assert.False(string.IsNullOrWhiteSpace(chunk));
				}

				if (testOffsets)
				{
					// Act: chunks with offsets
					var chunksWithOffsets = chunker.Chunk(sample, out var offsets);

					Assert.Equal(chunksWithOffsets.Count, offsets.Count);

					for (int i = 0; i < chunksWithOffsets.Count; i++)
					{
						var chunk = chunksWithOffsets[i];
						var (start, end) = offsets[i];

						Assert.True(CharTokenCounter(chunk) <= chunkSize);
						Assert.Equal(chunk, sample.Substring(start, end - start));
						Assert.False(string.IsNullOrEmpty(chunk));
						Assert.False(string.IsNullOrWhiteSpace(chunk));
					}

					// Lowercased, whitespace-stripped recomposition property.
					var lowerNoWhitespace = string.Concat(
						sample.ToLowerInvariant()
							  .Where(c => !char.IsWhiteSpace(c)));

					var chunksLower = chunker.Chunk(lowerNoWhitespace, out var offsetsLower, overlap: null);
					Assert.Equal(lowerNoWhitespace, string.Concat(chunksLower));
					Assert.Equal(
						lowerNoWhitespace,
						string.Concat(offsetsLower.Select(o => lowerNoWhitespace.Substring(o.Start, o.End - o.Start))));
				}
			}
		}
	}

	// --------------------------------------------------------------------
	// 2. Overlap behavior: high overlap yields more chunks than low overlap.
	// --------------------------------------------------------------------

	[Fact]
	public void Chunker_Overlap_HighVsLow_ForCharCounter()
	{
		// Arrange
		var chunker = ChunkerFactory.Create(CharTokenCounter, DeterministicChunkSize);

		// Low overlap (10% of chunk size)
		var lowOverlapChunks = chunker.Chunk(DeterministicInput, overlap: 0.1);

		// High overlap (ceil(0.9 * chunkSize) tokens)
		var highOverlap = Math.Ceiling(DeterministicChunkSize * 0.9);
		var highOverlapChunks = chunker.Chunk(DeterministicInput, overlap: highOverlap);

		// For char counter, we expect more chunks with high overlap than with low overlap.
		Assert.True(highOverlapChunks.Count > lowOverlapChunks.Count);

		// With offsets
		var lowOverlapChunksWithOffsets = chunker.Chunk(DeterministicInput, out var lowOffsets, overlap: 0.1);
		var highOverlapChunksWithOffsets = chunker.Chunk(DeterministicInput, out var highOffsets, overlap: highOverlap);

		Assert.True(highOverlapChunksWithOffsets.Count > lowOverlapChunksWithOffsets.Count);
		Assert.True(highOffsets.Count > lowOffsets.Count);

		// Chunks must match the text slices described by the offsets.
		Assert.Equal(
			highOverlapChunksWithOffsets,
			highOffsets.Select(o => DeterministicInput.Substring(o.Start, o.End - o.Start)).ToList());
	}

	// --------------------------------------------------------------------
	// 3. Deterministic behavior for char token counter.
	// --------------------------------------------------------------------

	[Fact]
	public void Chunker_Deterministic_ForCharCounter()
	{
		// Arrange
		var chunker = ChunkerFactory.Create(CharTokenCounter, DeterministicChunkSize);

		// Act
		var chunks = chunker.Chunk(DeterministicInput);

		// Assert
		Assert.Equal(DeterministicCharChunks, chunks);

		// With offsets
		var chunks2 = chunker.Chunk(DeterministicInput, out var offsets);
		Assert.Equal(DeterministicCharChunks, chunks2);
		Assert.Equal(DeterministicCharOffsets, offsets);
	}

	// --------------------------------------------------------------------
	// 4. Direct call into ChunkerCore.Chunk with memoize=true matches Chunker behavior.
	// --------------------------------------------------------------------

	[Fact]
	public void ChunkerCore_Chunk_Memoized_Matches_Chunker()
	{
		// Arrange
		var chunker = ChunkerFactory.Create(CharTokenCounter, DeterministicChunkSize);
		var expectedChunks = chunker.Chunk(DeterministicInput, out var expectedOffsets);

		// Act
		var coreResult = ChunkerCore.Chunk(
			text: DeterministicInput,
			chunkSize: DeterministicChunkSize,
			tokenCounter: CharTokenCounter,
			memoize: true,
			returnOffsets: true);

		// Assert
		Assert.Equal(expectedChunks, coreResult.Chunks);
		Assert.Equal(expectedOffsets, coreResult.Offsets);
	}

	// --------------------------------------------------------------------
	// 5. Chunking multiple texts.
	// --------------------------------------------------------------------

	[Fact]
	public void Chunker_ChunkMany_Works_ForCharCounter()
	{
		// Arrange
		var chunker = ChunkerFactory.Create(CharTokenCounter, DeterministicChunkSize);
		var inputs = new[] { DeterministicInput, DeterministicInput };

		// Act
		var chunksMany = chunker.ChunkMany(inputs);
		var chunksManyWithOffsets = chunker.ChunkMany(inputs, out var offsetsMany);

		// Assert
		Assert.Equal(2, chunksMany.Count);
		Assert.All(chunksMany, chunks => Assert.Equal(DeterministicCharChunks, chunks));

		Assert.Equal(2, offsetsMany.Count);
		Assert.All(offsetsMany, offsets => Assert.Equal(DeterministicCharOffsets, offsets));
	}

	// --------------------------------------------------------------------
	// 6. Edge cases: empty string and whitespace-only.
	// --------------------------------------------------------------------

	[Fact]
	public void Chunker_Handles_Empty_And_Whitespace()
	{
		var chunkSize = 512;
		var chunker = ChunkerFactory.Create(CharTokenCounter, chunkSize);

		// Empty string: should not throw.
		var emptyChunks = chunker.Chunk(string.Empty);
		Assert.Empty(emptyChunks);

		// Whitespace: should not throw; may return empty due to filtering of whitespace-only chunks.
		var wsChunks = chunker.Chunk("\n\n");
		Assert.All(wsChunks, c => Assert.False(string.IsNullOrWhiteSpace(c)));
	}
}
