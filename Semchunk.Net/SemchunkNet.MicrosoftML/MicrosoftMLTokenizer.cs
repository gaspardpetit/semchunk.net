using Microsoft.ML.Tokenizers;

namespace SemchunkNet.MicrosoftML;

/// <summary>
/// ITokenizer implementation backed by Microsoft.ML.Tokenizers.
/// </summary>
public sealed class MicrosoftMLTokenizer : SemchunkNet.ITokenizer
{
	private readonly Tokenizer _tokenizer;

	public MicrosoftMLTokenizer(
		Tokenizer tokenizer,
		int modelMaxLength,
		int? maxTokenChars = null,
		int? fixedSpecialTokenCount = null)
	{
		_tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));

		if (modelMaxLength <= 0)
			throw new ArgumentOutOfRangeException(nameof(modelMaxLength), "Model max length must be positive.");

		ModelMaxLength = modelMaxLength;
		MaxTokenChars = maxTokenChars;

		if (fixedSpecialTokenCount.HasValue)
		{
			FixedSpecialTokenCount = fixedSpecialTokenCount.Value;
		}
		else
		{
			try
			{
				FixedSpecialTokenCount = _tokenizer.EncodeToIds(string.Empty).Count;
			}
			catch
			{
				// Fallback: treat as zero special tokens if empty input isn't supported.
				FixedSpecialTokenCount = 0;
			}
		}
	}

	/// <summary>
	/// Create a tokenizer using Microsoft.ML's built-in tiktoken implementation.
	/// </summary>
	public static MicrosoftMLTokenizer ForTiktokenModel(
		string modelName = "gpt-4o",
		int modelMaxLength = 128000,
		int? maxTokenChars = null,
		int? fixedSpecialTokenCount = null)
	{
		if (string.IsNullOrWhiteSpace(modelName))
			throw new ArgumentException("Model name must be non-empty.", nameof(modelName));

		var tokenizer = TiktokenTokenizer.CreateForModel(modelName);
		return new MicrosoftMLTokenizer(tokenizer, modelMaxLength, maxTokenChars, fixedSpecialTokenCount);
	}

	public int[] Encode(string text)
	{
		var ids = _tokenizer.EncodeToIds(text);
		return ids is int[] arr ? arr : ids.ToArray();
	}

	public int ModelMaxLength { get; }

	public int? MaxTokenChars { get; }

	public int? FixedSpecialTokenCount { get; }
}
