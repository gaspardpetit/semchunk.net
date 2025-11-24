using System.IO.Compression;
using Tiktoken;
using Xunit.Abstractions;

namespace SemchunkNet.Tests;

public class BenchmarkTests
{
	private readonly ITestOutputHelper _output;

	public BenchmarkTests(ITestOutputHelper output)
	{
		_output = output;
	}

	// Raw NLTK Gutenberg zip
	private const string GutenbergUrl =
		"https://github.com/nltk/nltk_data/raw/refs/heads/gh-pages/packages/corpora/gutenberg.zip";

	// Where to put the downloaded zip & extracted files (relative to test bin)
	private static readonly string DataRoot =
		Path.Combine(AppContext.BaseDirectory, "testdata");

	private static readonly string ZipPath =
		Path.Combine(DataRoot, "gutenberg.zip");

	private static readonly string ExtractedPath =
		Path.Combine(DataRoot, "gutenberg");

	/// <summary>
	/// Ensure the Gutenberg corpus is downloaded and extracted.
	/// Returns the directory containing the .txt files.
	/// </summary>
	public static async Task<string> EnsureDownloadedAsync()
	{
		Directory.CreateDirectory(DataRoot);

		if (!Directory.Exists(ExtractedPath))
		{
			if (!File.Exists(ZipPath))
			{
				await DownloadZipAsync();
			}

			ExtractZip();
		}

		return ExtractedPath;
	}

	/// <summary>
	/// Synchronous convenience wrapper for tests that aren't async.
	/// </summary>
	public static string EnsureDownloaded()
	{
		return EnsureDownloadedAsync()
			.GetAwaiter()
			.GetResult();
	}

	private static async Task DownloadZipAsync()
	{
		Console.WriteLine($"Downloading Gutenberg corpus from {GutenbergUrl} ...");

		using var http = new HttpClient();
		using var response = await http.GetAsync(GutenbergUrl);
		response.EnsureSuccessStatusCode();

		await using var fs = File.Create(ZipPath);
		await response.Content.CopyToAsync(fs);

		Console.WriteLine($"Saved Gutenberg zip to {ZipPath}");
	}

	private static void ExtractZip()
	{
		Console.WriteLine($"Extracting Gutenberg corpus to {ExtractedPath} ...");

		if (Directory.Exists(ExtractedPath))
		{
			Directory.Delete(ExtractedPath, recursive: true);
		}

		ZipFile.ExtractToDirectory(ZipPath, ExtractedPath);

		Console.WriteLine("Extraction complete.");
	}

	/// <summary>
	/// Load all .txt files as strings.
	/// </summary>
	public static List<string> LoadAllTexts()
	{
		var root = EnsureDownloaded();

		var texts = new List<string>();
		var files = Directory.GetFiles(root, "*.txt", SearchOption.AllDirectories);

		foreach (var file in files)
		{
			try
			{
				var text = File.ReadAllText(file);
				if (!string.IsNullOrWhiteSpace(text))
				{
					texts.Add(text);
				}
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine($"Failed to read {file}: {ex.Message}");
			}
		}

		return texts;
	}

	[Fact]
	public void Chunker_Benchmark_On_Gutenberg_CharCounter()
	{
		// Arrange
		var texts = LoadAllTexts();
		Assert.NotEmpty(texts); // sanity

		// Build a token-counter function.
		var encoder = ModelToEncoder.For("gpt-4");

		Func<string, int> tokenCounter = s => encoder.CountTokens(s);
		const int chunkSize = 512;

		var chunker = ChunkerFactory.Create(tokenCounter, chunkSize);

		// Act
		var sw = System.Diagnostics.Stopwatch.StartNew();
		var allChunks = chunker.ChunkMany(texts);
		sw.Stop();

		// Assert-ish: mostly we want timing output for manual comparison
		_output.WriteLine($"Gutenberg benchmark: {sw.Elapsed.TotalSeconds:F2}s");
		_output.WriteLine($"Total texts: {texts.Count}");
		_output.WriteLine($"Total chunks: {allChunks.Sum(c => c.Count)}");
	}
}

