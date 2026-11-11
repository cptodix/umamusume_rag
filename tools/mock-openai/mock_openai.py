#!/usr/bin/env python3
"""Minimal mock of an OpenAI-compatible API for LLM-free pipeline tests.

Serves:
  POST /v1/embeddings          -> deterministic pseudo-embeddings (hash of the text)
  POST /v1/chat/completions    -> a fixed canned answer

Why: lets you exercise the whole RAG pipeline (ingest -> embed -> store ->
retrieve -> prompt -> answer) without LM Studio or any real model. The vectors
are random-ish and therefore meaningless for retrieval quality — this is a
plumbing test, not an accuracy test.

Usage:
  python3 tools/mock-openai/mock_openai.py [--port 8123] [--dim 768]

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

ARGS = argparse.ArgumentParser()
ARGS.add_argument("--port", type=int, default=8123)
ARGS.add_argument("--dim", type=int, default=768, help="embedding dimension to return")
OPTIONS = ARGS.parse_args()


def embedding(text: str):
    """Deterministic vector per text, so re-ingesting gives identical results."""
    seed = int(hashlib.sha256(text.encode("utf-8")).hexdigest()[:8], 16)
    rng = random.Random(seed)
    return [rng.random() for _ in range(OPTIONS.dim)]


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
            use_base64 = body.get("encoding_format") == "base64"
            data = []
            for index, text in enumerate(inputs):
                values = embedding(text)
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
    print(f"mock OpenAI API on http://127.0.0.1:{OPTIONS.port}/v1 (dim={OPTIONS.dim})")
    HTTPServer(("127.0.0.1", OPTIONS.port), Handler).serve_forever()
