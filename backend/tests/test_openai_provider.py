"""Exercises the real adapter with only fictional keys and canned HTTP replies."""

import http.client
import io
import json
import threading
import unittest
import urllib.error
from unittest.mock import patch

from backend.openai_provider import (DEFAULT_MODEL, ENDPOINT, MAX_REPLY, PROMPT_VERSION,
                                     OpenAIProvider, ProviderFailure)
from backend.server import ROUTE, make_server
from backend.support import DISSENT, NO_AI, InvalidSupport
from backend.tests.test_support import fixture


KEY = "sk-fictional-for-tests"


def provider_body(message, plan, *, status="completed", refusal=False):
    encoded = json.dumps({"message": message, "recommended_plan": plan})
    content = ({"type": "refusal", "refusal": message} if refusal else
               {"type": "output_text", "text": encoded, "annotations": []})
    return json.dumps({
        "status": status,
        "output": [{"type": "message", "role": "assistant",
                    "status": status, "content": [content]}],
        "id": "resp_fake",
    }).encode()


class FakeTransport:
    def __init__(self, result):
        self.result = result
        self.calls = []

    def open(self, request, timeout):
        self.calls.append((request, timeout))
        if isinstance(self.result, Exception):
            raise self.result
        return io.BytesIO(self.result)


def fake_provider(body):
    transport = FakeTransport(body)
    return OpenAIProvider(DEFAULT_MODEL, KEY, opener=transport), transport


class OpenAIHarness(unittest.TestCase):
    def test_both_roles_share_snapshot_and_settings(self):
        plan = [{"target": "West Fork", "points": 2}]
        sample = "The larger reported branch is worth testing, but its access threshold is unknown."
        provider, transport = fake_provider(provider_body(sample, plan))
        data = fixture()
        direct = provider.generate(data)
        question = "How would you compare the reported branch counts if access takes more points than expected?"
        transport.result = provider_body(question, None)
        dissent = provider.generate(fixture(DISSENT))
        self.assertEqual(direct["recommended_plan"], plan)
        self.assertEqual(direct["message"], "Send 2 LP to West Fork. " + sample)
        self.assertEqual(direct["prompt_version"], PROMPT_VERSION)
        self.assertEqual(dissent["message"], question)
        self.assertIsNone(dissent["recommended_plan"])
        self.assertNotIn("output", direct)
        self.assertEqual(len(transport.calls), 2)
        payloads = []
        system_messages = []
        for req, timeout in transport.calls:
            self.assertEqual(req.get_header("Authorization"), f"Bearer {KEY}")
            self.assertEqual(req.full_url, ENDPOINT)
            self.assertEqual(req.get_method(), "POST")
            self.assertGreater(timeout, 0)
            self.assertNotIn(KEY.encode(), req.data)
            payload = json.loads(req.data)
            self.assertEqual(payload["model"], "gpt-5.4-mini")
            self.assertEqual(json.loads(payload["input"]), data["context"])
            self.assertEqual(payload["text"]["format"]["type"], "json_schema")
            self.assertTrue(payload["text"]["format"]["strict"])
            self.assertEqual(payload["reasoning"], {"effort": "none"})
            self.assertFalse(payload["store"])
            self.assertNotIn("tools", payload)
            system_messages.append(payload.pop("instructions"))
            payloads.append(payload)
        self.assertNotEqual(*system_messages)
        self.assertEqual(*payloads)

    def test_output_guards(self):
        sample = "A publicly reported count can still conceal an unknown threshold."
        cases = [
            (fixture(), b"not json"),
            (fixture(), b"x" * (MAX_REPLY + 1)),
            (fixture(), b'{"status":"completed","output":[]}'),
            (fixture(), b'{"status":"completed","output":[[]]}'),
            (fixture(), provider_body(sample, None)),
            (fixture(), provider_body(sample, [{"target": "Not a site", "points": 1}])),
            (fixture(), provider_body(sample, [{"target": "West Fork", "points": 3},
                                               {"target": "East Fork", "points": 2}])),
            (fixture(), provider_body(f"The server returned {KEY} and an unrelated response.", [])),
            (fixture(), provider_body("<b>This output contains markup.</b>", [])),
            (fixture(), provider_body("Too short", [])),
            (fixture(DISSENT), provider_body("Would the route still work if the threshold were higher?",
                                             [{"target": "West Fork", "points": 2}])),
            (fixture(DISSENT), provider_body("This is not a question about the decision.", None)),
        ]
        for index, (request, body) in enumerate(cases):
            with self.subTest(case=index):
                provider, transport = fake_provider(body)
                with self.assertRaisesRegex(ProviderFailure, "^invalid_provider_output$"):
                    provider.generate(request)
                self.assertEqual(len(transport.calls), 1)

    def test_disallowed_context_and_no_ai_never_reach_provider(self):
        provider, transport = fake_provider(b"")
        request = fixture()
        request["context"]["sites"][0]["true_survivor_location"] = "West Fork"
        with self.assertRaises(InvalidSupport):
            provider.generate(request)
        with self.assertRaisesRegex(ProviderFailure, "^unsupported_provider_condition$"):
            provider.generate(fixture(NO_AI))
        self.assertEqual(transport.calls, [])

    def test_safe_errors_without_retry(self):
        for problem, code in [
            (TimeoutError(KEY), "provider_timeout"),
            (urllib.error.URLError(TimeoutError(KEY)), "provider_timeout"),
            (urllib.error.URLError(KEY), "provider_unavailable"),
            (urllib.error.HTTPError(ENDPOINT, 429, KEY, {}, io.BytesIO(b"private data")),
             "provider_rejected"),
        ]:
            with self.subTest(code=code):
                provider, transport = fake_provider(problem)
                with self.assertRaisesRegex(ProviderFailure, f"^{code}$"):
                    provider.generate(fixture())
                self.assertEqual(len(transport.calls), 1)

    def test_configuration_requires_opt_in_and_key(self):
        with patch.dict("os.environ", {"OPENAI_API_KEY": KEY}, clear=True):
            with self.assertRaisesRegex(ValueError, "disabled"):
                OpenAIProvider.from_environment()
            with patch.dict("os.environ", {"CLSR_ALLOW_LIVE": "1"}):
                self.assertEqual(OpenAIProvider.from_environment().model, "gpt-5.4-mini")
                with patch.dict("os.environ", {"CLSR_OPENAI_MODEL": "gpt-5.4-mini-2026-03-17"}):
                    self.assertEqual(OpenAIProvider.from_environment().model,
                                     "gpt-5.4-mini-2026-03-17")
                with patch.dict("os.environ", {"CLSR_OPENAI_MODEL": "unknown-model"}):
                    with self.assertRaisesRegex(ValueError, "supported snapshot"):
                        OpenAIProvider.from_environment()
                with patch.dict("os.environ", {"OPENAI_API_KEY": ""}):
                    with self.assertRaisesRegex(ValueError, "OPENAI_API_KEY"):
                        OpenAIProvider.from_environment()

    def test_real_loopback_route_with_fake_openai(self):
        sample = "More people are reported on one branch, but we cannot infer their exact locations."
        provider, transport = fake_provider(provider_body(sample, []))
        with make_server(0, provider=provider) as server:
            worker = threading.Thread(target=server.serve_forever)
            worker.start()
            try:
                def post(request):
                    client = http.client.HTTPConnection("127.0.0.1", server.server_port, timeout=3)
                    try:
                        client.request("POST", ROUTE, json.dumps(request),
                                       {"Content-Type": "application/json"})
                        response = client.getresponse()
                        return response.status, json.load(response)
                    finally:
                        client.close()

                status, reply = post(fixture())
                self.assertEqual((status, reply["provider"]), (200, "openai"))
                self.assertEqual(reply["recommended_plan"], [])
                self.assertEqual(len(transport.calls), 1)

                status, reply = post(fixture(NO_AI))
                self.assertEqual((status, reply["provider"]), (200, "none"))
                self.assertEqual(len(transport.calls), 1)

                transport.result = b"invalid reply"
                self.assertEqual(post(fixture()), (502, {"error": "invalid_provider_output"}))
                transport.result = TimeoutError("private upstream message")
                self.assertEqual(post(fixture()), (504, {"error": "provider_timeout"}))
                transport.result = provider_body("private refusal", [], refusal=True)
                self.assertEqual(post(fixture()), (502, {"error": "provider_refused"}))
                transport.result = provider_body(sample, [], status="incomplete")
                self.assertEqual(post(fixture()), (502, {"error": "provider_incomplete"}))
                self.assertEqual(len(transport.calls), 5)
            finally:
                server.shutdown()
                worker.join()
