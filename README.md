# Semchunk.Net 🧩

> **Semchunk.Net** is a C#/.NET port of the original **[semchunk](https://github.com/isaacus-dev/semchunk)** library by *Isaacus* (Python).  
> All credit for the algorithm and design goes to the original author; this project re-implements it for the .NET ecosystem.

Semchunk.Net is a fast, lightweight, easy-to-use library for splitting text into **semantically meaningful chunks** in .NET.

- Works with **any** token counter (`Func<string,int>`)
- Plays nicely with tiktoken-style libraries (e.g. `Tiktoken`, `Microsoft.ML.Tokenizers`)
- Supports **overlapping chunks** and **character offsets** back into the original text
- Uses the same recursive, semantics-aware algorithm as the Python version

The goal is a faithful port of semchunk’s behaviour, with a .NET-idiomatic API.

---

## Installation 📦

```bash
dotnet add package Semchunk.Net
```

Or via PackageReference:

```xml
<ItemGroup>
  <PackageReference Include="Semchunk.Net" Version="1.0.0" />
</ItemGroup>
```

Semchunk.Net is designed to target:

- netstandard2.0 (broad compatibility: .NET Framework 4.6.1+, .NET Core 2.0+, etc.)
- plus a modern TFM (e.g. net8.0) for optimal performance if you multi-target.

You bring your own tokenizer/token counter; Semchunk.Net doesn’t force a specific tokenizer dependency.

# Quickstart 👩‍💻

Example 1 – Using a tiktoken-style tokenizer (e.g. Tiktoken)

```csharp
using Semchunk.Net;
using Tiktoken;

// Choose a model compatible with cl100k_base (e.g. "gpt-4").
var encoder = ModelToEncoder.For("gpt-4");

// Token counter: number of tokens in a string.
Func<string, int> tokenCounter = text => encoder.CountTokens(text);

// Choose a chunk size (in tokens).
const int chunkSize = 512;

// Construct a chunker.
var chunker = ChunkerFactory.Create(tokenCounter, chunkSize);

var text = "The quick brown fox jumps over the lazy dog.";

// Basic chunking (no overlap, no offsets):
var chunks = chunker.Chunk(text);

// Chunk with character offsets and 50% overlap:
var chunksWithOffsets = chunker.Chunk(
    text,
    out var offsets,
    overlap: 0.5
);

// Chunk a list of texts:
var manyChunks = chunker.ChunkMany(new[] { text });
```

Example 2 – Simple custom token counter
If you don’t care about true tokens and just want a quick splitter:

```csharp
using Semchunk.Net;

// Each word = 1 "token"
Func<string, int> wordCounter = s =>
    string.IsNullOrWhiteSpace(s)
        ? 0
        : s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;

const int chunkSize = 16;

var chunker = ChunkerFactory.Create(wordCounter, chunkSize);

var text = "The quick brown fox jumps over the lazy dog.";

// Non-overlapping chunks:
var chunks = chunker.Chunk(text);
// Overlapping chunks with offsets:
var overlapped = chunker.Chunk(text, out var offsets, overlap: 0.5);
```

## Usage 🕹️

### `ChunkerFactory.Create(...)`

This is the main entry point. It mirrors Python’s `chunkerify(...)`.

#### From a token counter

```csharp
public static Chunker Create(
    Func<string, int> tokenCounter,
    int chunkSize,
    int? maxTokenChars = null,
    bool memoize = true,
    int? cacheMaxSize = null
)
```
- `tokenCounter` – function that returns the token count for a string.
- `chunkSize` – max tokens per chunk.
- `maxTokenChars` – optional performance hint: longest token length in characters.

    If provided, Semchunk.Net can short-circuit tokenization for very long inputs, just like the Python version.

- `memoize` – whether to memoize the token counter (LRU-style cache).
- `cacheMaxSize` – reserved for future bounded-cache support (currently unbounded, as in Python’s default).

Returns a Chunker instance.

#### From a tokenizer abstraction

If you define an ITokenizer:

```csharp
public interface ITokenizer
{
    int[] Encode(string text);
    int ModelMaxLength { get; }
}
```

You can construct a Chunker directly:

```csharp
public static Chunker Create(
    ITokenizer tokenizer,
    int? chunkSize = null,
    int? maxTokenChars = null,
    bool memoize = true,
    int? cacheMaxSize = null
)
```

If chunkSize is null, Semchunk.Net uses `tokenizer.ModelMaxLength` (analogous to Python’s model_max_length heuristic).

You’re free to implement ITokenizer for:

- Tiktoken
- Microsoft.ML.Tokenizers / TiktokenTokenizer.CreateForModel("gpt-4")
- any other tokenizer you like.

### `Chunker`

This is the main object you work with once created.

#### Chunk a single text

```csharp
public IReadOnlyList<string> Chunk(
    string text,
    double? overlap = null
);
```

- `overlap`:
    - null → no overlap.
    - < 1.0 → treated as a ratio of chunkSize (e.g. 0.2 → 20% overlap).
    - >= 1.0 → treated as an absolute token count.

#### Chunk with offsets

```csharp
public IReadOnlyList<string> Chunk(
    string text,
    out IReadOnlyList<(int Start, int End)> offsets,
    double? overlap = null
);
offsets[i] = (start, end) such that
chunks[i] == text.Substring(start, end - start).
```

Offsets are character indices into the original string (0-based, end-exclusive).

#### Chunk multiple texts

```csharp
public IReadOnlyList<IReadOnlyList<string>> ChunkMany(
    IReadOnlyList<string> texts,
    double? overlap = null
);

public IReadOnlyList<IReadOnlyList<string>> ChunkMany(
    IReadOnlyList<string> texts,
    out IReadOnlyList<IReadOnlyList<(int Start, int End)>> allOffsets,
    double? overlap = null
);
```

Returns one list of chunks per input text.

In the offsets overload, `allOffsets[i]` corresponds to offsets for `texts[i]`.

### `ChunkerCore.Chunk(...)` (low-level API)

For advanced usage, you can call the algorithm directly:

```csharp
ChunkResult ChunkerCore.Chunk(
    string text,
    int chunkSize,
    Func<string, int> tokenCounter,
    bool memoize = true,
    bool returnOffsets = false,
    double? overlap = null,
    int? cacheMaxSize = null,
    int recursionDepth = 0,
    int startOffset = 0
);
```

Mirrors Python’s `semchunk.chunk(...)`.

Returns ChunkResult with:

```csharp
public readonly struct ChunkResult
{
    public IReadOnlyList<string> Chunks { get; }
    public IReadOnlyList<(int Start, int End)> Offsets { get; }
}
```

You usually don’t need this unless you’re doing very custom plumbing.

## How It Works 🔍

Semchunk.Net implements the same algorithm as the Python version:

- Pick the most meaningful splitter
- For each text, it chooses the best splitter in this order:
    - Largest run of newlines / carriage returns (`\n`, `\r`)
    - Largest run of tabs (`\t`)
    - Largest run of whitespace (`\s`); or, if the longest run is a single char and there exists whitespace preceded by one of the punctuation splitters below, those specific whitespace characters

    - Sentence terminators: `.`, `?`, `!`, `*`
    - Clause separators: `;`, `,`, `(`, `)`, `[`, `]`, `“`, `”`, `‘`, `’`, `'`, `"`, `` ` ``
    - Sentence interrupters: `:`, `—`, `…`
    - Word joiners: `/`, `\`, `–`, `&`, `-`
    - Fallback: individual characters

### Recursive splitting
- Text is split by the chosen splitter into pieces.

For any piece whose token count exceeds `localChunkSize`, Semchunk.Net recursively re-chunks that piece.

### Merge underfull splits

Adjacent splits are merged using a binary-search-like heuristic to approximate the target chunk size, using an adaptive tokens/characters ratio.

This continues until each chunk is at or below the desired token limit.

### Reattach punctuation

If the splitter is non-whitespace and it makes sense to do so, trailing splitters are attached to the preceding chunk without breaking the token budget.

Otherwise, the splitter becomes its own small chunk with proper offsets.

### Strip whitespace-only chunks

After the top-level pass, any chunks that are empty or consist only of whitespace are removed.

### Build overlapping windows (optional)

If overlap is set:

- The algorithm reduces the effective chunk size internally to `min(overlapTokens, chunkSize - overlapTokens)` where overlapTokens is:
    - `floor(chunkSize * overlap)` if `overlap < 1` (ratio), or
    - `min(overlap, chunkSize - 1)` if `overlap >= 1` (absolute tokens).

It first builds non-overlapping subchunks of size localChunkSize.

Then it merges groups of subchunks into overlapping windows, sliding by a stride derived from the non-overlapped portion so that each final chunk overlaps the previous by the specified amount.

The result is a sequence of chunks that respect a token budget but align much better with human sentence/paragraph structure than naive fixed-window or simple recursive character chunkers.

## Benchmarks 📊

The original Python semchunk README reports (on a Ryzen 9 7900X, 96 GB RAM, Python 3.12):

- ~3.0 s to chunk the entire NLTK Gutenberg corpus into 512-token GPT-4 chunks
- vs ~24.8 s for semantic-text-splitter under similar conditions

Semchunk.Net includes an analogous benchmark against the same corpus and a GPT-4-style tokenizer (via a .NET tiktoken implementation). The C# version appears to be at least as fast using Tiktoken (tryAGI):

Python version:
```
Number of texts: 18
semchunk: 2.71s, total chunks: 7390
semantic_text_splitter: 22.05s, total chunks: 7277
```

C# version:
```
Number of texts: 18
Semchunk.Net: 1.82s, total chunks: 7390
```

## Licence 📄

This project is licensed under the MIT License, consistent with the original semchunk library.

Please see LICENCE for details.
The core algorithm and design are by Isaacus (semchunk, Python); 
Semchunk.Net is an independent C#/.NET implementation of that work.
