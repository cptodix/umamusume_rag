# Next steps

## 1. Make it run

```sh
docker compose up -d                                  # PostgreSQL + pgvector on :5433
dotnet tool restore                                   # repo-local dotnet-ef
dotnet run --project src/UmamusumeRag.Api             # applies migrations on startup (Development)
```

Point the app at a model endpoint (LM Studio by default):

1. In LM Studio, load **one chat model and one embedding model**, then start the local server
   (default `http://localhost:1234/v1`).
2. Get the exact model identifiers — the UI's Developer/API tab shows them, or:

   ```sh
   curl http://localhost:1234/v1/models
   ```

3. Put them in `src/UmamusumeRag.Api/appsettings.json` → `Llm:ChatModel` and
   `Llm:EmbeddingModel`. (Using OpenAI or another provider instead: change `Llm:BaseUrl` and
   `Llm:ApiKey` — nothing else.)

Then ingest and ask (VS Code REST Client / Rider: `src/UmamusumeRag.Api/UmamusumeRag.Api.http`):

```sh
# Export your Google Docs: File → Download → Microsoft Word (.docx) (or Markdown)
# Drop them into the repo's data/ folder, then:
curl -X POST http://localhost:5033/api/documents/ingest

curl -X POST http://localhost:5033/api/ask \
  -H "Content-Type: application/json" \
  -d '{"question": "What is the URA Finals?"}'
```

## 2. Before you tune anything: build a baseline

This is the step most people skip, and it is what turns "I changed a number" into "I learned
something". Spend 20 minutes here.

1. Write 10–15 questions your documents *should* answer, in `data/` or a scratch file. Mix them up:
   - **lexical**: an exact term, e.g. `"URA Finals"`, a character name, a skill name
   - **semantic**: a paraphrase that shares no keywords with the source text
   - **multi-hop**: needs facts from two different documents
   - **unanswerable**: not in your docs at all (the system should say so)
2. For each, note which document/section *should* be retrieved.
3. Record today's behaviour: what `/api/ask` returns and, importantly, **which sources and scores**
   it cites. That `sources` array is your retrieval debugger — if the right chunk is not in the
   sources, the problem is retrieval, not the model.
4. Re-run this same set after every change below. Keep the results in a file.

## 3. Experiment roadmap

Ordered roughly by effort-to-insight ratio. Each item is small enough to finish in one sitting.

### 3.1 Chunking

**Change:** `Rag:ChunkSize` (800 → 300, 1500) and `Rag:ChunkOverlap` (120 → 0, 250).
Re-ingest with `?force=true`.

**Observe:** for the *same* question, do the retrieved sources get more/less precise? At 300 do
answers lose context that spans a boundary? At 1500 do the cited chunks contain the answer plus a
lot of irrelevant text? Check the `score` values — they shift too.

**Then:** make chunking structure-aware. Right now it's character-based. Try splitting markdown on
headings and treating each section as a unit before applying the recursive splitter. Compare
`ChunkSize` in characters vs. a rough token estimate (`chars / 4`).

### 3.2 Retrieval depth (Top-K)

**Change:** `{"question": "...", "topK": 1}` vs `topK: 10` (per-request override exists).

**Observe:** too small → the answer chunk is missing; too large → the model gets distracted and
may cite the wrong passage. Also watch latency: every extra chunk is more prompt tokens.

### 3.3 Grounding and prompting

**Change:** `RagQueryService.SystemPrompt` and how context is assembled in `AskAsync`.

**Try:** reorder context so the most similar chunk comes *last* (models attend more to the end of
long prompts), add "answer in one paragraph", or forbid quoting the context verbatim. Then ask an
**unanswerable** question and verify it declines instead of inventing an answer.

**Observe:** citation accuracy. The model should cite `[n]` matching the source list — check that
`[2]` really is where the claim came from.

### 3.4 Embedding model

**Change:** `Llm:EmbeddingModel` (e.g. `nomic-embed-text`, `bge-*`, `e5-*`, `all-minilm`), then
re-ingest with `?force=true`.

**Observe:** retrieval hit rate on your baseline set, especially the **lexical** questions — those
separate models most clearly. Also compare vector dimensions and ingest time.

**Gotcha:** stored vectors belong to the old model's space. Always `?force=true` after switching,
and never mix models in one database.

### 3.5 Add an ANN index (pgvector)

**Change:** the column is dimension-less today. Lock it to your model's dimension and add HNSW:

```sql
ALTER TABLE chunks ALTER COLUMN "Embedding" TYPE vector(768);   -- match your model
CREATE INDEX idx_chunks_embedding_hnsw ON chunks
    USING hnsw ("Embedding" vector_cosine_ops);
```

(The quoting matters — EF created PascalCase columns.)

**Observe:** `EXPLAIN ANALYZE` the retrieval query before/after: sequential scan → index scan.
Measure ingest time too — the index makes writes slower.

**Then:** tune `hnsw.ef_search` (higher = better recall, slower) and try `ivfflat` with `lists`
for comparison. Check whether recall on your baseline set actually improves, and by how much
latency drops.

### 3.6 Hybrid search (vector + lexical)

**Why:** embeddings are weak at exact strings — names, skill names, abbreviations. Full-text
search is weak at paraphrases. Combine them.

**Change:** add a PostgreSQL `tsvector` column (or a GIN index over `to_tsvector('english', "Content")`),
run a second query with `websearch_to_tsquery`, and merge the two ranked lists with
**Reciprocal Rank Fusion**. RRF is ~15 lines and needs no score calibration:

```
score(d) = Σ over lists  1 / (k + rank_in_list(d))     # k ≈ 60
```

**Observe:** your lexical questions should jump in hit rate. This is usually the single biggest
quality win after chunking.

### 3.7 Reranking

**Change:** retrieve `topK = 20` cheaply, then re-score those candidates and keep the best 5.
Options, easiest first:
- ask the chat model to score each candidate 0–10 against the question
- use a cross-encoder / reranker model served by LM Studio
- use an embedding-similarity re-rank with a larger, slower embedding model

**Observe:** does the correct chunk move up into the final top 5? Watch the added latency — it's
the main cost of reranking.

### 3.8 Query transformation

**Try, one at a time:**
- **Multi-query**: ask the model for 3 rephrasings of the question, retrieve for each, merge (RRF).
- **HyDE**: ask the model to write a hypothetical answer, embed *that*, and retrieve with it —
  often beats embedding the question.
- **Conversational rewriting**: if you add chat history later, rewrite follow-ups ("and its
  stamina requirement?") into standalone questions before retrieving.

**Observe:** gains on semantic and multi-hop questions; watch the extra model call's latency.

### 3.9 Evaluation harness

Turn your baseline into numbers so improvements are measurable:
- **Retrieval**: hit rate@k (is the expected chunk in the top k?) and MRR (mean reciprocal rank).
  This needs no LLM and catches most regressions.
- **Answer**: faithfulness (is every claim supported by the retrieved context?) and
  answer relevance. Either eyeball them on 15 questions or use an LLM-as-judge / a library.

Run this after every change in this list. Metrics you can't re-run aren't metrics.

### 3.10 After that

Streaming answers (SSE), conversation memory, chunk metadata + filters (by document/section/date),
parent-document retrieval (retrieve small, prompt big), deduplication of near-identical chunks,
caching of embeddings and answers, and background ingestion with progress reporting.

## 4. Day-to-day operations

| Task | How |
|---|---|
| Re-embed everything (after changing chunking or the embedding model) | `POST /api/documents/ingest?force=true` |
| Ingest a folder elsewhere | `POST /api/documents/ingest?path=/abs/path` |
| Ingest a single file | `POST /api/documents/upload` (multipart) — lands in `data/uploads/` |
| Remove a document | `DELETE /api/documents/{id}` (chunks cascade) |
| Wipe the database | `docker compose down -v && docker compose up -d` |
| Inspect chunks by hand | `docker exec -it umamusume-rag-postgres psql -U postgres -d umamusume_rag` |

## 5. Troubleshooting by symptom

| Symptom | Likely cause |
|---|---|
| `Failed ... Connection refused (localhost:1234)` | LM Studio's server isn't running |
| Answers ignore your documents | Nothing ingested, or embeddings came from a different model than the question |
| Ingest says `Skipped` after you edited the file | The extracted text hash didn't change (or it's genuinely identical) — use `?force=true` |
| Retrieval misses exact terms/names | Expected with pure vector search — that's the hybrid-search experiment |
| Retrieval degrades after switching models | Old vectors in the old space; re-ingest with `?force=true` |
| `expected N dimensions, not M` | The column was locked to a different dimension than your model produces |
| `column d.id does not exist` | Unquoted column names in the raw SQL — EF's columns are quoted PascalCase |
| "I don't have any indexed documents" | `/api/ask` ran before any successful ingest |
| Prompt too long / model truncates | Lower `TopK` or `ChunkSize` |
| Same answer for unrelated questions | Context is too large/diluted, or the model is ignoring the grounding instruction |

Next: `03-rag-resources.md` for the reading list behind each experiment.
