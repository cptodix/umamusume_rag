# What was built

RAG-specific documentation for this repo: the pipeline design, the decisions behind it,
what was actually verified, and which RAG gaps were deliberately left open.

> .NET / Web API specifics (minimal APIs, DI, EF Core basics, Docker) are not covered here.

## Pipeline at a glance

```
INGEST  (write path)                          ASK  (read path)
─────────────────────                         ─────────────────
file (.md/.txt/.docx)                         question
  │ load        IDocumentLoader                 │ embed question      IEmbeddingGenerator
  ▼                                            ▼
text  ──► SHA-256 hash ──► skip if unchanged  query vector
  │ chunk       ITextChunker                    │
  ▼                                            ▼
chunks ──► embed (1 batch call)               top-K chunks   pgvector `<=>` cosine search
  │                                            │
  ▼                                            ▼
documents + chunks + vectors → PostgreSQL     numbered context → grounded prompt
                                                │
                                                ▼
                                              IChatClient → answer + cited sources
```

| Stage | File | Notes |
|---|---|---|
| Load | `Infrastructure/Ingestion/TextDocumentLoader.cs`, `DocxDocumentLoader.cs` | `.md`, `.markdown`, `.txt`, `.docx` |
| Chunk | `Core/Services/RecursiveTextChunker.cs` | recursive splitter with overlap |
| Embed chunks | `Core/Services/DocumentIngestionService.cs` | one batched `GenerateAsync` call per document |
| Store | `Infrastructure/Persistence/PgVectorChunkRepository.cs` (`UpsertAsync`) | replace-by-source-path |
| Embed question | `Core/Services/RagQueryService.cs` | same model as ingestion (required) |
| Retrieve | `PgVectorChunkRepository.SearchChunksAsync` | raw SQL, cosine, `LIMIT topK` |
| Prompt + generate | `RagQueryService.AskAsync` | grounded system prompt, `Temperature = 0.1` |

## Ingestion details

1. **Hash first, embed later.** SHA-256 of the extracted text is stored in
   `documents."ContentHash"`. If the file's hash matches the stored one, ingestion returns
   `Skipped` and **no embedding call is made** — embeddings are the expensive part.
   `?force=true` bypasses this.
2. **Chunking** is recursive: it tries `\n\n`, `\n`, `. `, `? `, `! `, ` ` and finally a hard
   character split, merging pieces up to `Rag:ChunkSize` (800 chars).
3. **Overlap** (`Rag:ChunkOverlap`, 120 chars) is applied after splitting by prepending the
   tail of the previous chunk to the next one, trimmed so it never starts mid-word. This keeps
   sentences that straddle a boundary retrievable from both sides.
4. **One embedding request per document** (all chunks batched). The service asserts that the
   provider returned exactly one vector per chunk.
5. **Upsert semantics**: an existing document with the same `SourcePath` is deleted (its chunks
   cascade) and the new document inserted, so chunk ids and vectors are always consistent with
   the file on disk.

## Retrieval + generation details

The retrieval query is raw SQL, executed through EF Core's `Database.SqlQuery<T>`:

```sql
SELECT c."DocumentId"  AS "DocumentId",
       d."Title"       AS "DocumentTitle",
       d."SourcePath"  AS "SourcePath",
       c."ChunkIndex"  AS "ChunkIndex",
       c."Content"     AS "Content",
       1 - (c."Embedding" <=> {queryVector}) AS "Score"
FROM chunks c
JOIN documents d ON d."Id" = c."DocumentId"
ORDER BY c."Embedding" <=> {queryVector}
LIMIT {topK}
```

- `<=>` is pgvector's **cosine distance** operator. The API reports **similarity**:
  `score = 1 - distance`, so `1.0` = identical direction, `~0.0` = unrelated.
- No approximate index exists yet, so this is a sequential scan. Fine for a practice corpus;
  see `03-rag-resources.md` for HNSW.
- The prompt is built in `RagQueryService.AskAsync`: system prompt restricts the model to the
  numbered context, asks for `[n]` citations, and tells it to admit when the context lacks the
  answer. Retrieved chunks are numbered and labelled with their document title.
- Sources returned to the caller carry the document title, path, chunk index, a 300-char
  snippet, and the similarity score.

## Storage schema

| Table | Columns | Constraints |
|---|---|---|
| `documents` | `Id`, `Title`, `SourcePath`, `ContentHash`, `CreatedAt` | unique `SourcePath` |
| `chunks` | `Id`, `DocumentId`, `ChunkIndex`, `Content`, `Embedding` | FK → `documents` (`ON DELETE CASCADE`), unique (`DocumentId`, `ChunkIndex`) |

`Embedding` is a **dimension-less** `vector` column — intentional, so any embedding model works
without a migration. The trade-off: HNSW/IVFFlat indexes require a fixed dimension, so you must
lock it before adding an index (see the resources doc).

## RAG-relevant design decisions

| Decision | Why |
|---|---|
| Hand-rolled pipeline instead of Semantic Kernel / a RAG framework | Every stage stays visible and swappable — the point of the exercise |
| `Microsoft.Extensions.AI` abstractions (`IChatClient`, `IEmbeddingGenerator`) | Provider-agnostic: DeepInfra, LM Studio, OpenAI, anything OpenAI-compatible, via config only |
| One OpenAI-compatible client for both chat and embeddings | The default provider (DeepInfra) serves both; no second SDK to learn |
| EF Core owns storage; **no** `Microsoft.Extensions.VectorData` | Avoids two competing data layers over the same tables |
| Raw SQL for the vector search instead of a LINQ distance function | Makes the `<=>` operator and the distance→similarity conversion explicit. `c.Embedding.CosineDistance(q)` is the equivalent LINQ form if you prefer it |
| `float[]` in the domain entity + a value converter to `pgvector.Vector` | Keeps `UmamusumeRag.Core` free of database-specific types |
| Chunks are embedded, documents are not | Standard "small-to-big"-free baseline: the retrieved unit *is* the prompted unit |
| Content-hash skip | Avoids paying for embeddings on every re-run |
| Similarity (not distance) in the API response | Higher = better matches intuition and makes thresholds meaningful |
| MassTransit v8 mediator for endpoint → pipeline calls | Decouples HTTP from pipeline logic and makes the entry point swappable for a broker; v8 is pinned because v9+ is commercial |
| EF Core LINQ for metadata, raw SQL only for the vector search | LINQ distance functions require `Pgvector.Vector` on the entity, which would leak the database type into Core |

## RAG-relevant configuration

| Key | Default | Effect on retrieval quality |
|---|---|---|
| `Rag:ChunkSize` | 800 | Too large → diluted embeddings; too small → missing context |
| `Rag:ChunkOverlap` | 120 | Prevents boundary-straddling answers from becoming unfindable |
| `Rag:TopK` | 5 | More context vs. more noise and tokens; overridable per request |
| `Llm:EmbeddingModel` | `BAAI/bge-m3` | Determines the vector space — **changing it invalidates all stored vectors** |
| `Llm:ChatModel` | `mistralai/Mistral-Small-24B-Instruct-2501` | Answer quality and instruction-following (grounding) |
| `Rag:DocumentPrefix` | *(empty)* | Model-specific prefix for chunks (E5 `passage: `, nomic `search_document: `) |
| `Rag:QueryPrefix` | *(empty)* | Matching prefix for questions (E5 `query: `, nomic `search_query: `) |
| `Rag:EmbeddingDimensions` | *(null)* | Optional Matryoshka truncation (e.g. 512); re-ingest after changing |
| `Rag:DataPath` | `../../data` | Where `/ingest` scans (relative to the API project dir) |

## What was verified

| Check | Result |
|---|---|
| Build | `0 warnings, 0 errors` |
| Migration + schema | `chunks."Embedding"` is a real pgvector `vector` column; `vector` extension installed |
| Write path | Document + 3 chunks inserted via the repository (exercises the `float[]` → `vector` converter) |
| Search ranking | Query aligned to chunk B → `0.999934`; unrelated chunks → `0.001316` / `0.000793` |
| Ingest of a real file | Sample `.md` → 5 chunks, stored, listed with correct title and chunk count |
| Full RAG flow | API run against a mock OpenAI-compatible server: `/api/ask` returned an answer plus 5 ranked sources with snippets and scores |
| Delete | `DELETE /api/documents/{id}` → `204`, chunks removed by cascade |
| Failure mode | With no LLM endpoint running, ingest returns a per-file `Failed` result instead of throwing |
| Prefixes + truncation | Provider received `passage: …` for chunks and `query: …` for the question, `dimensions=512`, and the DB stored 512-dim vectors |
| Mediator dispatch | Ingest / ask / upload endpoints ran through `IRequestClient<T>`; consumer log lines confirmed dispatch and responses |

## RAG gotchas discovered while building this

1. **Cosine similarity ignores magnitude.** A first synthetic test used vectors that differed only
   in scale; every score came back ≈`1.0`. Cosine only cares about *direction*. Real embeddings
   from different texts differ in direction, but this is worth remembering when you sanity-check
   scores.
2. **Changing the embedding model requires re-ingesting.** Stored vectors live in the old model's
   space; comparing them to a new model's query vector is meaningless. Also, the hash-skip means
   `POST /ingest` alone will *not* re-embed — you must pass `?force=true`.
3. **EF Core creates quoted PascalCase columns.** The raw retrieval SQL must use `c."Embedding"`,
   `d."Title"`, … Unquoted `d.title` folds to lowercase and fails with
   `column d.id does not exist`.
4. **The embedding column has no fixed dimension.** Great for swapping models, but it blocks
   HNSW/IVFFlat until you lock it.
5. **Converted collection properties need a value comparer**, otherwise EF can't detect changes
   to the `float[]` embedding (a model-validation warning at startup).

## Deliberately not implemented (your RAG roadmap)

- No ANN index (sequential scan only)
- No hybrid / lexical search (so exact terms like "URA Finals" rely purely on the embedding)
- No reranking stage
- No query transformation (HyDE, multi-query, conversational rewriting)
- No evaluation harness or metrics
- No streaming answers, no conversation memory, no multi-turn query condensation
- No chunk metadata for filtering (section, page, date) and no parent-document retrieval
- No token-aware chunking (character-based only)
- Ingestion is synchronous and request-bound; no background job or progress reporting
- No dedup of near-identical chunks across documents

Continue in `02-next-steps.md`.
