namespace SemchunkNet;

/// <summary>
/// Minimal tokenizer abstraction similar to semchunk expectations.
/// </summary>
public interface ITokenizer
{
	/// <summary>
	/// Tokenizes the text and returns token IDs (or just positions).
	/// </summary>
	int[] Encode(string text);

	/// <summary>
	/// Maximum number of tokens supported by the model.
	/// </summary>
	int ModelMaxLength { get; }

	/// <summary>
	/// Maximum number of characters in any single token, if known; used for long-input heuristics.
	/// Returns <c>null</c> if this information is not available.
	/// </summary>
#if NETSTANDARD2_1 || NETCOREAPP3_0_OR_GREATER || NET5_0_OR_GREATER
	int? MaxTokenChars => null;
#else
	int? MaxTokenChars { get; }
#endif

	/// <summary>
	/// Number of fixed special tokens the tokenizer always adds per input (e.g. BOS/EOS), if known.
	/// Returns <c>null</c> if this information is not available.
	/// </summary>
#if NETSTANDARD2_1 || NETCOREAPP3_0_OR_GREATER || NET5_0_OR_GREATER
	int? FixedSpecialTokenCount => null;
#else
	int? FixedSpecialTokenCount { get; }
#endif
}
