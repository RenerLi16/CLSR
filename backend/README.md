# CLSR AI backend — Stage 3: Unity mock connection

Run from the Unity project root. Python 3.10+; no dependencies or API key.

```sh
python3 -B -m unittest discover -s backend/tests -v
```

The tests start and stop their own loopback servers. The OpenAI checks inject a
fake provider response at the HTTP transport boundary. They never contact
OpenAI, read a real key, or incur charges. To run the mock server separately:

```sh
python3 -B -m backend.server --port 8787
```

In Unity Review setup, select **Use local mock backend** before starting the
walkthrough. With the option off, Unity uses offline samples. The option is
limited to the Editor and native development builds. It never selects OpenAI:
the request explicitly asks the backend to use the mock. No-AI stays offline
regardless of this setting. See [review instructions](../REVIEW.md).

## Request and response

`POST /v1/support` accepts the `rescue-support-v2` snapshot.
Both AI conditions accept the same inputs: the public site tree and
observations, day's logistics limits, provisional plan, and three anonymous,
structured initial judgments. Unknowns use JSON `null`. The server rejects
unexpected fields, so names, written responses and unrevealed scenario
definitions do not belong in the payload. It cannot verify that an observation
sent in an allowed field was actually visible in Unity.

The Unity client distinguishes initial branch reports from updated survivor
counts and sends only released route announcements. An announcement that a
route costs one point less does not reveal its absolute threshold. Cache rewards
are included only for currently visible, unclaimed storage markers. Private
judgments are limited to branch, structured reason and draft expedition plan;
confidence and written responses never leave Unity.

The adapter uses the same public game data, structured anonymous judgments and
generation settings for both roles. Only the role instruction differs. It
requests one short English JSON message. For direct recommendation the server
checks destination IDs, unique targets, expedition count and point limits,
then renders the plan from the checked values. An empty list means hold; a legal
plan can still be blocked by the terrain. Dissent must return no structured
plan and a question. The no-AI condition never calls the provider.

Provider replies that cannot be validated return a short error code; provider
timeouts return HTTP 504. There are no automatic retries or fallback messages.
Valid OpenAI responses include the configured model ID and prompt version. The
checks do not establish factual accuracy, strategic quality, or that a question
is free of implied advice. The server does not save responses or serve browsers.
Do not expose it on the internet.

## Configuration for a later approved live test

The default mode is mock. To select OpenAI later, the operator must start the
backend with `--provider openai` and set the following in its environment:

| Variable | Purpose |
| --- | --- |
| `CLSR_ALLOW_LIVE=1` | Explicitly enable billable requests. |
| `OPENAI_API_KEY` | Backend-only API key. Never put it in Unity, a build, chat, or source control. |
| `CLSR_OPENAI_MODEL` | Optional: defaults to `gpt-5.4-mini`; set `gpt-5.4-mini-2026-03-17` to pin the snapshot. No automatic fallback. |

The adapter uses the [Responses API](https://developers.openai.com/api/reference/resources/responses/methods/create)
with [Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=responses).
It makes a fresh request for each message and sets `store: false` (not a
guarantee of zero provider retention). The English prompts and the
[GPT-5.4 mini](https://developers.openai.com/api/docs/models/gpt-5.4-mini)
settings are development defaults, not final research settings. No live
OpenAI request was made in Stage 2.

## Unity harness

A separate harness starts the mock service and uses the Unity Editor to run
synthetic Cedar Valley cases on a temporary project copy. It requires an
installed, licensed Editor and free localhost port 8787. Stop any local mock
server before running it.

```sh
python3 -B -m backend.tests.run_unity_harness --unity /path/to/Unity
```

The harness checks snapshots before/after a blocked expedition, cache claim,
route report and reset. It exercises the Unity request and reading pause,
including a failed call. It does not click through the GUI or launch a WebGL
build.

## Next checkpoint

Once the Unity mock walkthrough has been verified, run an explicitly approved
live smoke test using fictional data. Then use a scenario-based evaluation
harness for both AI modes and set up human scoring for factual accuracy and
role fidelity before relying on generated messages.
