# CLSR AI backend — Stage 1: local support harness

Run from the Unity project root. Python 3.10+; no dependencies or API key.

```sh
python3 -B -m unittest discover -s backend/tests -v
```

The test suite starts its own loopback server on an available port and shuts it
down afterward. To run the mock server separately:

```sh
python3 -B -m backend.server --port 8787
```

## What this stage proves

`POST /v1/support` accepts the versioned rescue snapshot in the synthetic
fixture. Both AI conditions accept the same inputs: the public site tree and
observations, day's logistics limits, provisional plan, and three anonymous,
structured initial judgments. Unknowns use JSON `null`. The server rejects
unexpected fields, so names, written responses and unrevealed scenario
definitions do not belong in the payload. It cannot verify that an observation
sent in an allowed field was actually visible in Unity.

Direct recommendation returns one syntactically legal deployment plan (an empty
list means hold). A legal plan can still be blocked by the terrain. Dissent
returns a discussion question plus `null` in place of a plan; the neutral arm
returns neither advice nor a question. These are labeled test outputs, not a
model or a test of advice quality. This server doesn't save responses, serve
browsers, or connect to Unity or Qwen. Do not expose it on the internet.

## Next checkpoints

1. Implement the Qwen adapter and its output checks; verify against a fake
   provider first (no key, cost, or participant data).
2. Connect the review game. Test the Unity snapshot against what a player can
   actually see, including cache information and revealed route changes, plus
   skipped responses, duplicate requests, and intervention timing. The no-AI
   arm should stay local and not send a request.
3. Run an explicitly approved live smoke test using fictional data, then use
   a scenario-based evaluation harness for both AI modes. Set up human scoring
   for factual accuracy and role fidelity before relying on generated messages.
