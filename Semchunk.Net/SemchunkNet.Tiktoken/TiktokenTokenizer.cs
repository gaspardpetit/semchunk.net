using Tiktoken;

namespace SemchunkNet.Tiktoken;

/// <summary>
/// ITokenizer implementation backed by the Tiktoken package.
/// </summary>
public sealed class TiktokenTokenizer : SemchunkNet.ITokenizer
{
	private readonly Encoder _encoder;

	public TiktokenTokenizer(
		string modelName = "gpt-4",
		int modelMaxLength = 8192,
		int? maxTokenChars = null,
		int? fixedSpecialTokenCount = null)
	{
		if (string.IsNullOrWhiteSpace(modelName))
			throw new ArgumentException("Model name must be non-empty.", nameof(modelName));

		_encoder = ModelToEncoder.For(modelName);

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
				FixedSpecialTokenCount = _encoder.Encode("").Count;
			}
			catch
			{
				// Fallback: treat as zero special tokens if empty input isn't supported.
				FixedSpecialTokenCount = 0;
			}
		}
	}

	public int[] Encode(string text)
	{
		var ids = _encoder.Encode(text);
		return ids is int[] arr ? arr : ids.ToArray();
	}

	public int ModelMaxLength { get; }

	public int? MaxTokenChars { get; }

	public int? FixedSpecialTokenCount { get; }
}
