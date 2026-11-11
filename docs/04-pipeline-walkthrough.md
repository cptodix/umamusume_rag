# Pipeline walkthrough — every step, every file, every knob

Written for a .NET developer with zero AI/RAG background. It assumes you know
C#, DI, EF Core and HTTP — nothing else. Every AI term is explained in
`05-glossary.md`; every step below points at the exact file and method that
executes it, and at the place where you can intervene.

## The mental model (read this first)

- **Ingestion is an ETL job.** Read files → split into rows → compute a
  "semantic hash" for each row → write to a search index. That's it. The index
  happens to live in PostgreSQL.
- **An embedding is a semantic hash.** A function `text -> float[768]` where
  *similar meaning* lands in a *similar direction*. It is deterministic: the
  same text always produces the same vector.
- **A chunk is a row in that index.** It is both the unit of retrieval and the
  unit that gets pasted into the prompt.
- **Asking is a search + one LLM call.** Embed the question, find the nearest
  chunk vectors (cosine similarity), paste them into a prompt, ask the model to
  answer **only from them**. That's RAG: an open-book exam.
- **The model is stateless.** It knows nothing about your documents except what
  you put in the prompt. "The answer is wrong" is almost always a retrieval or
  prompt problem, not a model problem.

The pipeline has **8 steps**. Steps 1–4 run on ingest, 5–8 run on every question:

| # | Step | Runs on | File |
|---|---|---|---|
| 0 | Wiring / config | startup | `src/UmamusumeRag.Api/Program.cs` → `Infrastructure/ServiceCollectionExtensions.cs` |
| 1 | **Load** file → text | ingest | `Infrastructure/Ingestion/TextDocumentLoader.cs`, `DocxDocumentLoader.cs` |
| 2 | **Chunk** text → chunks | ingest | `Core/Services/RecursiveTextChunker.cs` |
| 3 | **Embed** chunks → vectors | ingest | `Core/Services/DocumentIngestionService.cs` (calls the AI client) |
| 4 | **Store** → PostgreSQL | ingest | `Infrastructure/Persistence/PgVectorChunkRepository.UpsertAsync` |
| 5 | **Embed question** | ask | `Core/Services/RagQueryService.AskAsync` |
| 6 | **Retrieve** top-K chunks | ask | `Infrastructure/Persistence/PgVectorChunkRepository.SearchChunksAsync` |
| 7 | **Prompt** (context + question) | ask | `Core/Services/RagQueryService.AskAsync` |
| 8 | **Generate** answer | ask | `Core/Services/RagQueryService.AskAsync` (calls the AI client) |

Every one of those files has comments tagged `RAG step N/8`, plus `TWEAK:`,
`WHY:` and `GOTCHA:` markers. Find them all with:

```sh
grep -rn "RAG step\|TWEAK\|WHY:\|GOTCHA" src/
```

---

# Step 0 — Wiring and configuration (startup)

**Entry point:** `src/UmamusumeRag.Api/Program.cs` → `AddInfrastructure(builder.Configuration)`
**File:** `src/UmamusumeRag.Infrastructure/ServiceCollectionExtensions.cs`

What happens:

1. `ConnectionStrings:Postgres` is read and `AppDbContext` is registered with
   `UseNpgsql(..., o => o.UseVector())` — this teaches Npgsql/EF Core about the
   pgvector `vector` type.
2. The `Rag` config section is bound into `RagOptions` (chunk size, overlap,
   top-K, data path, embedding prefixes, optional dimension truncation).
3. Loaders (step 1), the chunker (step 2), the repository (steps 4/6) and the
   two pipeline services are registered.
4. `AddRagMessaging()` registers the in-process MassTransit mediator and the
   consumers that adapt messages to those services (no broker involved).
5. `AddOpenAiCompatibleAi` registers `IChatClient` and `IEmbeddingGenerator`
   against `Llm:BaseUrl` (steps 3, 5 and 8 all use these two instances). It
   fails fast if `Llm:ApiKey` is missing — the key comes from user-secrets or
   the `Llm__ApiKey` environment variable, never from the repo.
6. In Development, `Program.cs` applies pending EF Core migrations.

**Intervene here if you want to** replace a whole stage (new chunker, different
store, OCR loader), add caching/decorators, or change DI lifetimes.

**Config:** `src/UmamusumeRag.Api/appsettings.json` → `ConnectionStrings`, `Llm`, `Rag`.
The API key is supplied separately: `dotnet user-secrets set "Llm:ApiKey" "<token>"`.

---

# Ingest (write path)

HTTP: `POST /api/documents/ingest?path=&force=`
Entry point: `Api/Endpoints/DocumentsEndpoints.cs` → `IngestAsync`
→ MassTransit mediator (`Core/Messaging/IngestDocuments`)
→ `Infrastructure/Messaging/IngestDocumentsConsumer`
→ `Core/Services/DocumentIngestionService.cs`

The endpoint resolves the folder and sends an `IngestDocuments` message; the
consumer adapts it to the pipeline. A consumer exception propagates back through
the mediator and becomes a `ProblemDetails` response.

`IngestDirectoryAsync` enumerates the folder recursively and keeps files that
some registered loader accepts. Then it calls `IngestFileAsync` once per file.
All four steps below happen inside `IngestFileAsync`.

> **GOTCHA:** files nobody can load (e.g. `.pdf` today) are **skipped silently** —
> they don't appear in the response at all.

## Step 1 — Load: file → plain text

**File:** `Infrastructure/Ingestion/TextDocumentLoader.cs` (`.txt`, `.md`, `.markdown`),
`Infrastructure/Ingestion/DocxDocumentLoader.cs` (`.docx`)

What happens: the first loader whose `CanLoad(path)` returns true reads the file
and returns `LoadedDocument(Title, SourcePath, Content)`.

- Title: first Markdown `# ` heading, docx metadata title, or file name.
- Everything downstream only sees `Content` as a `string`. Images, charts and
  layout are **not** extracted.

**Intervene here if you want to:**

| Goal | Where |
|---|---|
| Support a new format (PDF, HTML, …) | add `IDocumentLoader` in `Infrastructure/Ingestion/` + register it in `ServiceCollectionExtensions` |
| Better titles / strip Markdown syntax | `TextDocumentLoader.LoadAsync` |
| Tables/headings from Word | `DocxDocumentLoader.LoadAsync` |
| OCR or image captioning | a new loader (or a post-load step) — the rest of the pipeline is unchanged |

**Debug:** if a file is missing from the ingest response, no loader matched its
extension.

## Step 2 — Chunk: text → chunk strings

**File:** `Core/Services/RecursiveTextChunker.cs` → `Chunk()` → `SplitRecursive()` → `ApplyOverlap()`

What happens:

1. Splits on the most natural boundary first: paragraph (`\n\n`), then line,
   then sentence (`. `, `? `, `! `), then word, then a hard character cut.
2. Merges small pieces back up to `Rag:ChunkSize` characters.
3. Prepends the tail of each chunk (up to `Rag:ChunkOverlap` characters, never
   starting mid-word) to the next one.

**Intervene here if you want to:**

| Goal | Where |
|---|---|
| Change sizes | `Rag:ChunkSize` / `Rag:ChunkOverlap` (then re-ingest with `?force=true`) |
| Change boundary priorities | the `Separators` array |
| Different overlap behavior | `ApplyOverlap` |
| A different algorithm entirely | implement `ITextChunker` and swap the registration |

**Debug:** the log line `Embedding {Count} chunks from {Path}` tells you how
many chunks a file produced. To *see* the chunks, query the `chunks` table.

## Step 3 — Embed: chunks → vectors

**File:** `Core/Services/DocumentIngestionService.cs` → the `embeddingGenerator.GenerateAsync(...)` call
**Client:** `Infrastructure/Ai/AiServiceCollectionExtensions.cs` (registered in step 0)

What happens: all chunks of one document are sent in **one batched request** to
the OpenAI-compatible `/v1/embeddings` endpoint using `Llm:EmbeddingModel`.
The provider returns one `float[dimension]` per chunk. The code asserts the
count matches and converts `ReadOnlyMemory<float>` → `float[]`.

**Intervene here if you want to:**

| Goal | Where |
|---|---|
| Try a different embedding model | `Llm:EmbeddingModel` (**then re-ingest with `?force=true`**) |
| Add model-specific prefixes (E5/nomic) | `Rag:DocumentPrefix` — and `Rag:QueryPrefix` in step 5 |
| Shrink vectors (Matryoshka truncation) | `Rag:EmbeddingDimensions` (e.g. 512) — re-ingest |
| Batch size / parallelism / retries | the `GenerateAsync` call and the loop in `IngestDirectoryAsync` |
| Local ONNX embeddings instead of HTTP | register a different `IEmbeddingGenerator` |

> **GOTCHA:** the embedding model used for ingestion **must** be the same one
> used to embed questions (step 5). The DI registration deliberately shares one
> instance for both. Changing the model invalidates every stored vector.

## Step 4 — Store: document + chunks + vectors → PostgreSQL

**File:** `Infrastructure/Persistence/PgVectorChunkRepository.cs` → `UpsertAsync`
**Schema:** `Infrastructure/Persistence/Configurations/*.cs`

What happens:

1. An existing document with the same `SourcePath` is deleted (its chunks
   cascade-delete, so no stale vectors survive).
2. The new `Document` with its `Chunk` list is inserted in one `SaveChanges`.
3. Each `Chunk.Embedding` (`float[]`) is converted to `pgvector.Vector` by
   `FloatArrayToVectorConverter` and written to the `vector` column.

Resulting row shape (abbreviated):

```
documents: Id | Title | SourcePath (unique) | ContentHash | CreatedAt
chunks:    Id | DocumentId (FK, cascade) | ChunkIndex | Content | Embedding (vector)
```

**Intervene here if you want to:**

| Goal | Where |
|---|---|
| Add chunk metadata (page, section, kind) | `Chunk` entity + `ChunkConfiguration` + a migration |
| Change replace-vs-update semantics | `UpsertAsync` |
| Store embeddings elsewhere | implement `IDocumentRepository` |

**Debug:** inspect rows with `psql` (see "Watching it happen" below).

---

# Ask (read path)

HTTP: `POST /api/ask` with `{ "question": "...", "topK": 5 }`
Entry point: `Api/Endpoints/AskEndpoints.cs` → `AskAsync`
→ MassTransit mediator (`Core/Messaging/AskQuestion`)
→ `Infrastructure/Messaging/AskQuestionConsumer`
→ `Core/Services/RagQueryService.cs` → `AskAsync`

## Step 5 — Embed the question

**File:** `Core/Services/RagQueryService.cs` → `AskAsync`

`embeddingGenerator.GenerateAsync([question])` → one vector, using the same
`Llm:EmbeddingModel` as step 3. `Rag:QueryPrefix` is prepended here (it must
mirror `Rag:DocumentPrefix`; both are empty for BGE-M3/Qwen3).

**Intervene:** you generally shouldn't — except for *query transformation*
(rewriting, multi-query, HyDE) which happens **before** this call. See
`02-next-steps.md` §3.8.

## Step 6 — Retrieve top-K chunks (the vector search)

**File:** `Infrastructure/Persistence/PgVectorChunkRepository.cs` → `SearchChunksAsync`

This is the heart of retrieval. One SQL statement:

```sql
ORDER BY c."Embedding" <=> @queryVector   -- cosine distance, ascending = best first
LIMIT @topK
```

and `score = 1 - distance`, so the API's `score` is **cosine similarity**
(1.0 = identical direction, ~0 = unrelated).

- `topK` comes from `Rag:TopK`, or per request via `AskRequest.TopK`.
- There is **no ANN index** yet: this is an exact scan of every chunk. Fine for
  a practice corpus; adding HNSW is `02-next-steps.md` §3.5.

**Intervene here if you want to:**

| Goal | Where |
|---|---|
| Retrieve more/fewer chunks | `Rag:TopK` or `{"topK": n}` |
| Change the metric | `<=>` → `<->` (L2) or `<#>` (inner product) in the SQL |
| Add a metadata filter | add a `WHERE` clause + parameters |
| Hybrid search (text + vector) | second query with `to_tsvector` + RRF merge — §3.6 |
| Rerank | raise `topK`, then re-score in `RagQueryService` — §3.7 |
| ANN index | lock the dimension + `CREATE INDEX ... USING hnsw` — §3.5 |

**Debug:** the returned `sources` array (step 8) shows exactly which chunks and
scores the model saw.

## Step 7 — Build the prompt (the "open book")

**File:** `Core/Services/RagQueryService.cs` → `AskAsync` (context assembly) and the `SystemPrompt` constant

What happens: retrieved chunks become numbered blocks

```
[1] (from "Document title")
<chunk text>
```

which are inserted into a user message together with the question, after a
system message that says: answer **only** from the context, cite `[n]`, and say
so if the answer is missing.

**Intervene here if you want to:**

| Goal | Where |
|---|---|
| Change grounding rules / tone / language | `SystemPrompt` |
| Change context format or order | the `context` string assembly |
| Trim or summarize context | before building `messages` |
| Few-shot examples | add more `ChatMessage`s |

This is the highest-leverage string in the project: most "it made something up"
problems are fixed here.

## Step 8 — Generate the answer

**File:** `Core/Services/RagQueryService.cs` → `chatClient.GetResponseAsync(...)`
**Client:** `Infrastructure/Ai/AiServiceCollectionExtensions.cs`

One `/v1/chat/completions` call with `Temperature = 0.1` (low = factual, not
creative). Then the response is shaped into `AskResponse(Answer, Sources)`,
where each source carries document title, path, chunk index, a 300-char snippet
and its similarity score.

**Intervene here if you want to:**

| Goal | Where |
|---|---|
| Change the chat model | `Llm:ChatModel` |
| Temperature / max tokens | the `ChatOptions` object |
| Streaming (SSE) | `GetStreamingResponseAsync` + an SSE endpoint |
| More/less detail in sources | the `SourceReference` projection |

---

# "Follow one file through the system"

Concrete trace for `data/sample-umamusume-overview.md`:

1. `POST /api/documents/ingest` → `IngestDirectoryAsync` finds the `.md` file.
2. `TextDocumentLoader` returns ~2.4 KB of text, title = the first `# ` heading.
3. SHA-256 of the text → no matching `documents."ContentHash"` → proceed.
4. `RecursiveTextChunker` with 800/120 produces **5 chunks**.
5. One embeddings request → 5 vectors (e.g. 768 floats each).
6. `UpsertAsync` inserts 1 document + 5 chunk rows in one transaction.
7. Response: `[{ "status": "Ingested", "chunkCount": 5, ... }]`.

Then `POST /api/ask`:

1. Question is embedded → one vector.
2. SQL orders all 5 chunks by `<=>` and takes the top 5 (with `topK: 5`).
3. The chunks are numbered and pasted into the prompt.
4. The model answers with `[n]` citations.
5. Response includes `answer` + 5 `sources` with scores.

Re-running ingest without changes returns `Skipped` and makes **no** embedding
call. Re-running with `?force=true` re-embeds and replaces the rows.

---

# Watching it happen (debugging toolkit)

| Tool | How |
|---|---|
| EF Core SQL log | `appsettings.Development.json` → `"Microsoft.EntityFrameworkCore.Database.Command": "Information"` |
| Pipeline logs | the API console: `Embedding N chunks from ...`, `Ingested ...`, `Retrieved N chunks ...` |
| Inspect the index | `docker exec -it umamusume-rag-postgres psql -U postgres -d umamusume_rag` then `\d chunks`, `SELECT count(*) FROM chunks;` |
| See actual chunks | `SELECT "DocumentId", "ChunkIndex", left("Content", 120) FROM chunks ORDER BY "DocumentId", "ChunkIndex";` |
| Check vector dimensions | `SELECT vector_dims("Embedding"), count(*) FROM chunks GROUP BY 1;` |
| See the search plan | `EXPLAIN ANALYZE SELECT ... ORDER BY "Embedding" <=> '[...]' LIMIT 5;` |
| Trace a request | breakpoints in `IngestFileAsync` (steps 1–4) or `RagQueryService.AskAsync` (steps 5–8) |
| Trust the sources array | if the right chunk isn't in `sources`, the bug is retrieval, not the model |

---

# Invariants — don't break these

1. **One embedding space per database.** Never change `Llm:EmbeddingModel`
   without re-ingesting (`?force=true`). Mixing vectors from two models produces
   meaningless scores with no error.
2. **A chunk's `Content` and `Embedding` must describe the same text.** Never
   edit one without recomputing the other.
3. **Chunking changes require re-ingest.** The hash-skip compares extracted
   text, not chunk configuration.
4. **Raw SQL must quote PascalCase column names** (`"Embedding"`, `"Id"`, …) —
   EF Core created them that way.
5. **Core must stay free of EF Core / Npgsql / OpenAI packages.** The pipeline
   talks only to `IDocumentRepository`, `IEmbeddingGenerator` and `IChatClient`.
6. **Package versions live in `Directory.Packages.props`**, never in csproj files.
7. **The `vector` column is dimension-less on purpose.** Lock it only when you
   add an ANN index.
8. **Prefixes and dimensions are part of the embedding configuration.**
   `Rag:DocumentPrefix` and `Rag:QueryPrefix` must mirror each other, and
   changing either prefix or `Rag:EmbeddingDimensions` requires a re-ingest.

Next: `05-glossary.md` for the vocabulary, `02-next-steps.md` for experiments,
`03-rag-resources.md` for the reading list.
