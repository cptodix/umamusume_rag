#!/usr/bin/env python3
"""Minimal mock of an OpenAI-compatible API for LLM-free pipeline tests.

Serves:
  POST /v1/embeddings          -> deterministic pseudo-embeddings (hash of the text)
  POST /v1/chat/completions    -> a fixed canned answer

Why: lets you exercise the whole RAG pipeline (ingest -> embed -> store ->
retrieve -> prompt -> answer) without a real model provider. The vectors are
random-ish and therefore meaningless for retrieval quality — this is a plumbing
test, not an accuracy test.

It honours the OpenAI `dimensions` parameter (returns that many floats), so
Matryoshka truncation (`Rag:EmbeddingDimensions`) can be tested too.

Usage:
  python3 tools/mock-openai/mock_openai.py [--port 8123] [--dim 768] [--verbose]

Then run the API against it:
  Llm__BaseUrl=http://127.0.0.1:8123/v1 Llm__ApiKey=mock \
  Llm__ChatModel=mock-chat Llm__EmbeddingModel=mock-embed \
    dotnet run --project src/UmamusumeRag.Api

Always delete documents ingested with the mock afterwards: their random vectors
poison the index, and the content-hash skip would prevent re-embedding with a
real model unless you pass ?force=true.
"""

import argparse
import base64
import hashlib
import json
import random
import struct
from http.server import BaseHTTPRequestHandler, HTTPServer

PARSER = argparse.ArgumentParser()
PARSER.add_argument("--port", type=int, default=8123)
PARSER.add_argument("--dim", type=int, default=768, help="default embedding dimension")
PARSER.add_argument("--verbose", action="store_true", help="log request summaries")
OPTIONS = PARSER.parse_args()


def embedding(text: str, dimensions: int):
    """Deterministic vector per text, so re-ingesting gives identical results."""
    seed = int(hashlib.sha256(text.encode("utf-8")).hexdigest()[:8], 16)
    rng = random.Random(seed)
    values = [rng.random() for _ in range(OPTIONS.dim)]
    # Emulate Matryoshka truncation: keep the first N components.
    return values[:dimensions] if dimensions < len(values) else values + [0.0] * (dimensions - len(values))


def encode_floats(values):
    return base64.b64encode(struct.pack(f"<{len(values)}f", *values)).decode()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_POST(self):
        length = int(self.headers.get("Content-Length", 0))
        body = json.loads(self.rfile.read(length) or b"{}")

        if self.path.endswith("/embeddings"):
            inputs = body.get("input")
            if isinstance(inputs, str):
                inputs = [inputs]
            dimensions = body.get("dimensions") or OPTIONS.dim
            use_base64 = body.get("encoding_format") == "base64"

            if OPTIONS.verbose:
                preview = (inputs[0] if inputs else "")[:80].replace("\n", " ")
                print(f"embeddings: {len(inputs)} input(s), dims={dimensions}, model={body.get('model')}, first={preview!r}", flush=True)

            data = []
            for index, text in enumerate(inputs):
                values = embedding(text, dimensions)
                entry = {"object": "embedding", "index": index}
                entry["embedding"] = encode_floats(values) if use_base64 else values
                data.append(entry)
            response = {
                "object": "list",
                "data": data,
                "model": body.get("model", "mock"),
                "usage": {"prompt_tokens": 0, "total_tokens": 0},
            }
        elif self.path.endswith("/chat/completions"):
            if OPTIONS.verbose:
                print(f"chat: model={body.get('model')}, messages={len(body.get('messages', []))}", flush=True)
            response = {
                "id": "chatcmpl-mock",
                "object": "chat.completion",
                "created": 0,
                "model": body.get("model", "mock"),
                "choices": [
                    {
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": "MOCK ANSWER: received the question and its context.",
                        },
                        "finish_reason": "stop",
                    }
                ],
                "usage": {"prompt_tokens": 0, "completion_tokens": 0, "total_tokens": 0},
            }
        else:
            self.send_response(404)
            self.end_headers()
            return

        payload = json.dumps(response).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)


if __name__ == "__main__":
    print(f"mock OpenAI API on http://127.0.0.1:{OPTIONS.port}/v1 (default dim={OPTIONS.dim})", flush=True)
    HTTPServer(("127.0.0.1", OPTIONS.port), Handler).serve_forever()
