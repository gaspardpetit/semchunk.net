namespace SemchunkNet;

/// <summary>
/// Minimal tokenizer abstraction similar to semchunk expectations.
/// </summary>
public interface ITokenizer
{
	/// <summary>Tokenizes the text and returns token IDs (or just positions).</summary>
	int[] Encode(string text);

	/// <summary>Maximum number of tokens supported by the model.</summary>
	int ModelMaxLength { get; }
}
