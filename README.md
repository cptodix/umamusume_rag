# umamusume_rag

A .NET 10 boilerplate for practicing **Retrieval-Augmented Generation (RAG)** over
Umamusume: Pretty Derby (Global) documentation.

The RAG pipeline is hand-rolled so every stage is visible in code:
chunking, embedding, storage, vector search, prompt construction, and generation.

## Documentation

| Doc | Contents |
|---|---|
| [`docs/01-what-was-built.md`](docs/01-what-was-built.md) | The RAG pipeline design, the decisions behind it, what was verified, and RAG gotchas |
| [`docs/02-next-steps.md`](docs/02-next-steps.md) | Run it, build a baseline, then a concrete experiment roadmap (chunking → hybrid search → reranking → evaluation) |
| [`docs/03-rag-resources.md`](docs/03-rag-resources.md) | Curated RAG reading list: guides, papers, indexes, evaluation, implementations |

## Architecture

```
                 POST /api/documents/ingest | /upload
                              │
              ┌───────────────▼────────────────┐
              │  DocumentIngestionService      │   Core/Services
              │  load → chunk → embed → store  │
              └───────┬───────────┬────────────┘
                      │           │
        IDocumentLoader│           │IEmbeddingGenerator  (Microsoft.Extensions.AI)
   (.md/.txt/.docx)   │           │
                      │           ▼
                      │     OpenAI-compatible endpoint
                      │     (LM Studio / OpenAI / ...)
                      ▼
              ┌────────────────────────┐
              │  PostgreSQL + pgvector │   chunks.embedding = vector
              └───────────┬────────────┘
                          │ cosine search (<=>)
                 POST /api/ask
                          │
              ┌───────────▼────────────┐
              │  RagQueryService       │   Core/Services
              │  embed → retrieve →    │
              │  prompt → generate     │
              └────────────────────────┘
```

| Project | Purpose |
|---|---|
| `src/UmamusumeRag.Api` | Minimal API endpoints, configuration, DI wiring |
| `src/UmamusumeRag.Core` | Entities, abstractions, chunker, ingestion + query pipeline (no DB/AI SDK dependencies) |
| `src/UmamusumeRag.Infrastructure` | EF Core + pgvector storage, document loaders, OpenAI-compatible AI clients |

## How the RAG pipeline maps to code

| RAG stage | Where |
|---|---|
| Load documents | `Infrastructure/Ingestion/TextDocumentLoader.cs`, `DocxDocumentLoader.cs` |
| Chunk text | `Core/Services/RecursiveTextChunker.cs` |
| Embed chunks | `Core/Services/DocumentIngestionService.cs` (via `IEmbeddingGenerator`) |
| Store chunks + vectors | `Infrastructure/Persistence/PgVectorChunkRepository.cs` (EF Core + pgvector) |
| Embed the question | `Core/Services/RagQueryService.cs` |
| Vector search (top-K) | `PgVectorChunkRepository.SearchChunksAsync` (pgvector `<=>` operator) |
| Prompt + generate | `RagQueryService.AskAsync` (grounded system prompt + numbered context) |

## Prerequisites

- .NET 10 SDK
- Docker (for the PostgreSQL + pgvector container)
- An OpenAI-compatible endpoint — either:
  - **LM Studio** (default): start the local server, load a chat model and an embedding
    model, then put their identifiers into `Llm:ChatModel` / `Llm:EmbeddingModel`.
  - **Online provider** (OpenAI, OpenRouter, ...): set `Llm:BaseUrl`, `Llm:ApiKey`,
    and the model names in `appsettings.json`.

## Quickstart

```sh
# 1. Start PostgreSQL + pgvector
docker compose up -d

# 2. Restore the repo-local dotnet-ef tool (first time only)
dotnet tool restore

# 3. Configure your LLM endpoint + model names
#    -> src/UmamusumeRag.Api/appsettings.json  (Llm section)

# 4. Run the API (applies EF Core migrations automatically in Development)
dotnet run --project src/UmamusumeRag.Api
```

Then use `src/UmamusumeRag.Api/UmamusumeRag.Api.http` (VS Code REST Client / Rider) or curl:

```sh
# Ingest everything under ./data
curl -X POST http://localhost:5033/api/documents/ingest

# Ask a question
curl -X POST http://localhost:5033/api/ask \
  -H "Content-Type: application/json" \
  -d '{"question": "What is the URA Finals?"}'
```

## Getting your Google Docs in

1. In Google Docs: **File → Download → Microsoft Word (.docx)** (or Markdown for simple docs).
2. Drop the files into the `data/` folder (subfolders are scanned recursively).
3. `POST /api/documents/ingest` — unchanged files are skipped automatically
   (content is hashed); pass `?force=true` to re-embed everything.

## Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `/health` | Database connectivity check |
| `POST` | `/api/documents/ingest?path=&force=` | Scan a folder and ingest all supported files |
| `POST` | `/api/documents/upload` | Upload a single file (multipart) and ingest it |
| `GET` | `/api/documents` | List ingested documents + chunk counts |
| `DELETE` | `/api/documents/{id}` | Delete a document and its chunks |
| `POST` | `/api/ask` | `{ "question": "...", "topK": 5 }` → answer + sources |

OpenAPI document (Development): `http://localhost:5033/openapi/v1.json`

## Configuration (`appsettings.json`)

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:Postgres` | `localhost:5433/umamusume_rag` | Matches `compose.yaml` (host port 5433) |
| `Llm:BaseUrl` | `http://localhost:1234/v1` | LM Studio default; swap for any OpenAI-compatible URL |
| `Llm:ApiKey` | `lm-studio` | LM Studio ignores it; required by the client |
| `Llm:ChatModel` | `qwen/qwen3-8b` | Replace with the identifier shown in LM Studio |
| `Llm:EmbeddingModel` | `text-embedding-nomic-embed-text-v1.5` | Replace with your embedding model |
| `Rag:ChunkSize` | `800` | Target chunk length (characters) |
| `Rag:ChunkOverlap` | `120` | Repeated characters between neighboring chunks |
| `Rag:TopK` | `5` | Chunks retrieved per question (overridable per request) |
| `Rag:DataPath` | `../../data` | Folder scanned by `/ingest`, relative to the API project directory (the app content root) — so the default points at the repo's `data/` folder. Absolute paths also work. |

## Things to try next

See [`docs/02-next-steps.md`](docs/02-next-steps.md) for the experiment roadmap (chunking,
retrieval depth, embedding models, HNSW indexing, hybrid search, reranking, query
transformation, evaluation) and [`docs/03-rag-resources.md`](docs/03-rag-resources.md) for the
reading list behind each step.
