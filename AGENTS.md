# AGENTS.md

Operating guide for AI agents (and humans) working in this repository. Read this
before changing code; it encodes the invariants that keep the RAG pipeline
correct and the conventions the docs rely on.

## What this repository is

A .NET 10 learning project for **Retrieval-Augmented Generation (RAG)** over
Umamusume: Pretty Derby (Global) documents. The pipeline is deliberately
hand-rolled: no Semantic Kernel, no Microsoft.Extensions.VectorData, no agent
framework. Every RAG stage is explicit code that a learner can read and modify.

Audience for explanations: an 8-year .NET developer with enterprise experience
and **zero** AI/RAG background. Explain AI concepts in .NET terms; do not explain
C#, DI, EF Core basics or HTTP.

## Golden rules — do not break these

1. **Dependency direction.** `UmamusumeRag.Core` must not reference EF Core,
   Npgsql, pgvector, `OpenAI`, or `Microsoft.Extensions.AI.OpenAI`. It may use
   `Microsoft.Extensions.AI.Abstractions` and `Logging.Abstractions`. Storage and
   model calls live in `UmamusumeRag.Infrastructure`.
2. **One embedding space per database.** Ingestion and query embedding must use
   the same `Llm:EmbeddingModel`. Never register two different embedding
   generators for the two paths.
3. **Changing chunking or the embedding model requires re-ingest.** The
   content-hash skip means `POST /ingest` alone will not re-embed; callers must
   pass `?force=true`. Do not "fix" this by weakening the hash check.
4. **Raw SQL must quote PascalCase identifiers** (`"Embedding"`, `"Id"`,
   `"DocumentId"`). EF Core created those column names; unquoted identifiers
   fold to lowercase and fail.
5. **Package versions only in `Directory.Packages.props`.** Never put a
   `Version=` attribute on a `PackageReference` (central package management).
6. **The `vector` column stays dimension-less** unless the task is explicitly to
   add an ANN index (which requires a fixed dimension).
7. **Comment convention.** Pipeline code is annotated with grep-able tags:
   `RAG step N/8`, `TWEAK:`, `WHY:`, `GOTCHA:`. Keep them accurate; the docs and
   learners rely on them. `grep -rn "RAG step\|TWEAK\|WHY:\|GOTCHA" src/`.
8. **No new frameworks.** Do not introduce Semantic Kernel, VectorData, LangChain
   or an agent framework unless the user explicitly asks. The point of the repo
   is visible, hand-rolled code.
9. **No silent behavior changes in ingest.** Per-file failures stay per-file
   (`IngestStatus.Failed`); a batch must not abort on one bad document.

## Commands

```sh
# build (must stay at 0 warnings, 0 errors)
dotnet build

# database (pgvector on host port 5433)
docker compose up -d
docker compose ps
docker compose down -v          # wipes data

# EF Core (repo-local tool)
dotnet tool restore
dotnet ef migrations list -p src/UmamusumeRag.Infrastructure -s src/UmamusumeRag.Api
dotnet ef migrations add <Name> -p src/UmamusumeRag.Infrastructure -s src/UmamusumeRag.Api --output-dir Persistence/Migrations
dotnet ef database update -p src/UmamusumeRag.Infrastructure -s src/UmamusumeRag.Api

# run (Development auto-applies migrations)
dotnet run --project src/UmamusumeRag.Api

# inspect the index
docker exec -it umamusume-rag-postgres psql -U postgres -d umamusume_rag
```

## Repository map

```
src/UmamusumeRag.Core/              # domain + pipeline (no DB/AI SDK deps)
  Abstractions/                     # IDocumentLoader, ITextChunker, IDocumentRepository
  Entities/                         # Document, Chunk (float[] embedding)
  Models/                           # RagOptions, API contracts
  Services/                         # RecursiveTextChunker, DocumentIngestionService, RagQueryService
src/UmamusumeRag.Infrastructure/    # implementations
  Ingestion/                        # TextDocumentLoader (.txt/.md), DocxDocumentLoader (.docx)
  Persistence/                      # AppDbContext, configs, migrations, PgVectorChunkRepository
  Ai/                               # OpenAI-compatible IChatClient + IEmbeddingGenerator
  ServiceCollectionExtensions.cs    # the DI wiring hub
src/UmamusumeRag.Api/               # minimal API
  Endpoints/                        # DocumentsEndpoints (write path), AskEndpoints (read path)
  Program.cs, appsettings.json
docs/                               # learner docs (see index below)
tools/mock-openai/                  # fake OpenAI server for LLM-free testing
data/                               # source documents (repo root; Rag:DataPath = ../../data)
```

## Pipeline map (the canonical 8 steps)

| # | Step | File |
|---|---|---|
| 0 | Wiring/config | `Api/Program.cs` → `Infrastructure/ServiceCollectionExtensions.cs` |
| 1 | Load | `Infrastructure/Ingestion/*.cs` |
| 2 | Chunk | `Core/Services/RecursiveTextChunker.cs` |
| 3 | Embed chunks | `Core/Services/DocumentIngestionService.cs` |
| 4 | Store | `Infrastructure/Persistence/PgVectorChunkRepository.UpsertAsync` |
| 5 | Embed question | `Core/Services/RagQueryService.AskAsync` |
| 6 | Retrieve | `Infrastructure/Persistence/PgVectorChunkRepository.SearchChunksAsync` |
| 7 | Prompt | `Core/Services/RagQueryService.AskAsync` |
| 8 | Generate | `Core/Services/RagQueryService.AskAsync` |

## Change recipes

| Task | Do this |
|---|---|
| Tune chunking | `Rag:ChunkSize` / `Rag:ChunkOverlap`; re-ingest `?force=true` |
| New chunking algorithm | implement `ITextChunker`; swap registration in `ServiceCollectionExtensions` |
| New file format | implement `IDocumentLoader` in `Infrastructure/Ingestion/`; register it |
| Change top-K | `Rag:TopK` or per-request `{"topK": n}` |
| Change similarity metric | SQL in `SearchChunksAsync`: `<=>` ↔ `<->` ↔ `<#>` |
| Change prompt/grounding | `RagQueryService.SystemPrompt` + context assembly |
| Change chat model / temperature | `Llm:ChatModel` / `ChatOptions` in `AskAsync` |
| Swap LLM provider | `Llm:BaseUrl` + `Llm:ApiKey` + model IDs (no code) |
| Add chunk metadata | `Chunk` entity + `ChunkConfiguration` + migration |
| Add HNSW index | lock dimension in `ChunkConfiguration`, add migration/SQL, then `EXPLAIN ANALYZE` |
| Add hybrid search | second query (`to_tsvector`) + RRF merge in `SearchChunksAsync` |
| Add reranking | raise `topK`, re-score in `RagQueryService` before prompt assembly |
| Streaming answers | `IChatClient.GetStreamingResponseAsync` + SSE endpoint |
| Evaluation harness | new script/endpoint using the `sources` array (hit rate@k, MRR) |

## Verification protocol (after any change)

1. `dotnet build` → **0 warnings, 0 errors**.
2. `docker compose up -d` and wait for healthy.
3. If entities/configurations changed: add + apply a migration; verify with
   `psql \d chunks`.
4. **LLM-free end-to-end check** (requires no LM Studio):

   ```sh
   python3 tools/mock-openai/mock_openai.py &          # fake OpenAI on :8123
   Llm__BaseUrl=http://127.0.0.1:8123/v1 Llm__ApiKey=mock \
   Llm__ChatModel=mock-chat Llm__EmbeddingModel=mock-embed \
     dotnet run --project src/UmamusumeRag.Api --no-build &
   sleep 8
   curl -s -X POST localhost:5033/api/documents/ingest
   curl -s localhost:5033/api/documents
   curl -s -X POST localhost:5033/api/ask -H 'Content-Type: application/json' \
        -d '{"question":"What is the URA finals?"}'
   ```

   Expect: ingest `Ingested` with a chunk count, one document listed, and an
   answer plus `sources` with scores.
5. **Clean up.** Delete the test document (`DELETE /api/documents/{id}`) and stop
   both processes. Mock embeddings are random: leaving them in the DB poisons
   retrieval and the hash-skip will prevent re-embedding with a real model
   unless `?force=true` is used.
6. If you touched the SQL: also verify the exact query in `psql` and check the
   column quoting.

## Known gotchas

- Content root is the API **project** directory, so `Rag:DataPath` is
  `../../data` (repo-root `data/`).
- `POST /api/documents/upload` saves into `data/uploads/` (gitignored).
- Unsupported extensions are skipped silently by `/ingest`; `/upload` returns a
  `Failed` result instead.
- `dotnet ef` requires `Microsoft.EntityFrameworkCore.Design` in the **startup**
  project (`UmamusumeRag.Api`), not just in Infrastructure.
- EF Core is pinned explicitly in Infrastructure (10.0.15) so the Api project
  doesn't resolve the older version Npgsql's minimum dependency asks for.
- Cosine similarity ignores magnitude — synthetic test vectors that differ only
  in scale all score ≈1.0.
- No ANN index exists; retrieval is an exact sequential scan.

## Documentation index

| Doc | Purpose |
|---|---|
| `docs/01-what-was-built.md` | Design decisions, verification results, RAG gotchas |
| `docs/02-next-steps.md` | Setup, baseline building, experiment roadmap, troubleshooting |
| `docs/03-rag-resources.md` | Curated RAG reading list |
| `docs/04-pipeline-walkthrough.md` | Step-by-step trace with files and intervention points |
| `docs/05-glossary.md` | AI/RAG vocabulary for a .NET developer |
| `docs/agent/teaching-notes.md` | Curriculum + diagnostic flow for teaching this repo |

## Teaching contract (when acting as a teacher)

- Anchor every explanation to a **file + method** in this repo, then to the
  corresponding concept in `05-glossary.md`.
- Always pair a concept with a **measurable experiment** from
  `02-next-steps.md` — change one knob, re-run the baseline, compare.
- Use the `sources` array as the primary diagnostic tool: retrieval vs.
  generation is the first question to answer for any bad answer.
- Do not add frameworks to "simplify" the lesson. Do not hide the pipeline
  behind an abstraction.
- When suggesting a change that invalidates stored vectors, always state the
  `?force=true` re-ingest requirement.
