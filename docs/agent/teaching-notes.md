# Teaching notes (for a future AI teacher)

A structured curriculum for teaching RAG with this repository. Written so an AI
assistant (or a human mentor) can pick it up cold and run a coherent course.

## Student profile

- 8 years of .NET, mostly enterprise systems (DI, EF Core, HTTP APIs, Docker).
- **Zero** AI/RAG knowledge. Do not assume familiarity with vectors, embeddings,
  tokens, cosine similarity, or model APIs.
- Learns by modifying code and observing behaviour, not by reading theory first.
- Wants to understand *why* a knob matters, not just that it exists.

## Teaching principles

1. **Anchor every idea to a file + method**, then to `docs/05-glossary.md`.
2. **One knob per experiment.** Change a single value, re-run the same question
   set, compare. Never bundle changes — the student must see cause and effect.
3. **Use the `sources` array as the ground truth.** For any bad answer, first ask
   "was the right chunk retrieved?" That splits the problem into retrieval vs.
   generation and prevents guessing.
4. **Use .NET analogies** from `05-glossary.md` (embedding = semantic hash,
   ANN index = B-tree for vectors, RAG = open-book exam).
5. **Never add a framework** to make a lesson easier. The repo's value is that
   the pipeline is visible.
6. **Always mention the re-ingest requirement** when a change invalidates stored
   vectors (`?force=true`).
7. **Measure before/after.** No lesson is complete without an observation.

## Lesson sequence

Each lesson is one sitting. The student should end with a changed file and a
recorded observation.

### Lesson 1 — What an embedding is

- **Concept:** `text -> float[N]`; similar meaning → similar direction.
  Deterministic, not an equality hash.
- **Anchor:** `AiServiceCollectionExtensions` (registration) and step 3 in
  `DocumentIngestionService.IngestFileAsync`.
- **Read:** `03-rag-resources.md` → "Start here" + "Embeddings".
- **Exercise:** ingest the sample doc, then in `psql`:
  `SELECT vector_dims("Embedding"), count(*) FROM chunks GROUP BY 1;`
- **Expected:** one dimension value (e.g. 768) for every chunk.
- **Misconception to correct:** "the model stores my document". It does not — the
  database does; the model only produced numbers.
- **Success check:** student can explain why two different sentences can have
  almost identical vectors.

### Lesson 2 — Chunking

- **Concept:** the chunk is both retrieval unit and prompt unit.
- **Anchor:** `RecursiveTextChunker.Chunk` / `Separators` / `ApplyOverlap`.
- **Exercise:** `ChunkSize` 800 → 300, re-ingest `?force=true`, ask the same
  question. Then 1500.
- **Expected:** `chunkCount` changes; at 300 sources are more precise but may
  lose context; at 1500 sources contain more irrelevant text and scores shift.
- **Misconception:** "more chunks is always better" — no, it changes both
  precision and token cost.
- **Success check:** student predicts which size will win for a short factual
  question vs. a multi-sentence explanation.

### Lesson 3 — Vector search and top-K

- **Concept:** cosine similarity, exact scan, `LIMIT k`.
- **Anchor:** `PgVectorChunkRepository.SearchChunksAsync` (the SQL).
- **Exercise:** `topK` 1 / 5 / 10 via the request body; compare `sources`.
- **Expected:** at 1 the answer chunk may be missing; at 10 the prompt carries
  noise and latency rises.
- **Misconception:** "score 0.8 means 80% correct" — it is a geometric
  similarity, not a probability.
- **Success check:** student explains why cosine ignores vector magnitude.

### Lesson 4 — Grounding and the prompt

- **Concept:** the model only knows what is in the prompt; the system prompt is
  the contract.
- **Anchor:** `RagQueryService.SystemPrompt` + context assembly.
- **Exercise:** ask a question that is not in the documents. Then tighten the
  prompt to refuse explicitly. Then ask a normal question.
- **Expected:** the model declines instead of inventing; citations stay accurate.
- **Misconception:** "the model looked it up" — no, it read the pasted chunks.
- **Success check:** student can point at the exact lines that prevent
  hallucination.

### Lesson 5 — Choosing an embedding model

- **Concept:** different models = different vector spaces; dimensions differ;
  switching requires re-ingestion.
- **Anchor:** `Llm:EmbeddingModel`; `DocumentIngestionService` (step 3) and
  `RagQueryService` (step 5) share one instance.
- **Exercise:** switch to another local embedding model, `?force=true`, re-run
  the baseline. Then compare an exact-name question vs. a paraphrased one.
- **Expected:** lexical questions separate models most clearly.
- **Misconception:** "just point the query at the new model" — that compares
  vectors from two different spaces and silently returns garbage.
- **Success check:** student states the re-ingest requirement unprompted.

### Lesson 6 — ANN indexing

- **Concept:** exact vs approximate search; recall/latency trade-off.
- **Anchor:** `ChunkConfiguration` (dimension-less column) and the SQL in
  `SearchChunksAsync`.
- **Exercise:** lock the dimension, create an HNSW index, `EXPLAIN ANALYZE`
  before/after, tune `hnsw.ef_search`.
- **Expected:** plan changes from sequential scan to index scan; ingest slows
  down; recall may drop slightly.
- **Misconception:** "indexes make retrieval better" — they make it faster, and
  can make it slightly less accurate.
- **Success check:** student can explain why the column had to be dimension-less
  before this lesson.

### Lesson 7 — Hybrid search

- **Concept:** embeddings are weak at exact strings; keyword search is weak at
  paraphrases; RRF merges ranked lists.
- **Anchor:** `SearchChunksAsync` (add a second query) and
  `IDocumentRepository` (extend the contract).
- **Exercise:** add `to_tsvector`/`websearch_to_tsquery` search and fuse with
  `1/(60 + rank)`.
- **Expected:** exact-term questions improve; paraphrased questions stay about
  the same.
- **Misconception:** "scores from the two lists can be averaged" — no, they are
  on different scales; fuse by rank.
- **Success check:** student can explain why RRF needs no calibration.

### Lesson 8 — Reranking

- **Concept:** cheap recall first, expensive precision second.
- **Anchor:** `RagQueryService.AskAsync` (between step 6 and step 7).
- **Exercise:** retrieve 20, re-score with the chat model (0–10), keep 5.
- **Expected:** the correct chunk moves into the final set more often; latency
  rises.
- **Misconception:** "reranking replaces retrieval" — it refines it.
- **Success check:** student measures both hit rate and added latency.

### Lesson 9 — Evaluation

- **Concept:** a fixed question set turns tuning into measurement.
- **Anchor:** the `sources` array; build a small harness (script or endpoint).
- **Exercise:** 15 questions (lexical, semantic, multi-hop, unanswerable);
  compute hit rate@k and MRR; record before/after for one earlier change.
- **Expected:** a baseline number the student can defend.
- **Misconception:** "the answers look better" — without numbers, changes are
  guesses.
- **Success check:** student re-runs the harness after a later change and
  reports a delta.

### Lesson 10 — Query transformation

- **Concept:** the question is not always the best retrieval query.
- **Anchor:** before step 5 in `RagQueryService.AskAsync`.
- **Exercise:** multi-query (3 rephrasings, merge with RRF) or HyDE (embed a
  hypothetical answer).
- **Expected:** semantic and multi-hop questions improve; one extra model call.
- **Misconception:** "more model calls always help" — measure cost and latency.
- **Success check:** student can state when *not* to use it (short factual,
  latency-sensitive questions).

## Diagnostic flow for a bad answer

Ask in this order, and stop at the first "yes":

1. **Was the right chunk in `sources`?**
   - **No** → retrieval problem. Check: chunking (lesson 2), topK (3), embedding
     model (5), need for hybrid search (7).
2. **Was the chunk present but the answer ignored it?**
   - → prompt/grounding problem (lesson 4), context ordering, or too much
     context diluting the signal (topK too high).
3. **Was the answer right but the citation wrong?**
   - → numbering/format bug in step 7, not a model problem.
4. **Was the chunk present, prompt fine, answer still wrong?**
   - → model quality/temperature (lesson 5/8), or the question is genuinely
     ambiguous.

Never start by changing the model.

## Assessment questions

1. Why must ingestion and querying use the same embedding model? *(Different
   models define different vector spaces; distances between them are
   meaningless.)*
2. What exactly does `score` mean in the API response? *(Cosine similarity,
   `1 - distance`; 1.0 = identical direction.)*
3. You change `Rag:ChunkSize` and call `/ingest` without `force`. What happens?
   *(Nothing — the content hash is unchanged, so files are skipped.)*
4. Why is there no ANN index in the repo? *(Exact scan is simpler and fine for a
   practice corpus; HNSW requires locking the dimension.)*
5. What does `topK` trade off? *(Recall of the needed chunk vs. noise, tokens,
   latency.)*
6. You need PDF support. What changes? *(One `IDocumentLoader` + one DI
   registration; nothing else.)*
7. Why does hybrid search help with "URA Finals" specifically? *(Embeddings are
   weak at exact strings; lexical search is strong there.)*
8. What does the `sources` array tell you that the answer text does not? *(What
   the model actually saw — retrieval ground truth.)*

## Anti-patterns to avoid when teaching

- Do not explain transformers, training, or tokenizers — not needed to use RAG.
- Do not suggest fine-tuning; it is the wrong tool for "my documents aren't in
  the model".
- Do not wrap the pipeline in a framework to "simplify" it.
- Do not change several knobs at once.
- Do not evaluate on the same questions used for tuning without saying so.
- Do not skip the cleanup step after mock-server tests (random vectors poison
  the index).
