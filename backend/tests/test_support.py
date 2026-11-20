"""Synthetic end-to-end checks over a real loopback HTTP connection."""

import http.client
import json
import threading
import unittest

from backend.server import MAX_BODY, ROUTE, make_server
from backend.support import VERSION


def fixture(condition="direct_recommendation"):
    def site(name, parent, count=None):
        return {
            "id": name, "parent": parent, "known_difficulty": None,
            "initial_branch_report": count, "initial_branch_note": None,
            "known_branch_remaining": None, "known_remaining_here": None,
            "known_remaining_beyond": None, "available_cache_reward": None,
        }

    west = [{"target": "West Fork", "points": 2}]
    east = [{"target": "East Fork", "points": 1}]
    return {
        "version": VERSION, "condition": condition,
        "context": {
            "day": 1, "total_days": 3, "points_available": 9, "daily_cap": 4,
            "max_expeditions": 2, "provisional_plan": west, "released_route_reports": [],
            "sites": [site("West Fork", "Camp", 3), site("Miner's Hut", "West Fork"),
                      site("East Fork", "Camp", 2)],
            "initial_judgments": [
                {"branch": "West Fork", "reason": "branch_count", "plan": west.copy()},
                {"branch": "East Fork", "reason": "access", "plan": east},
                {"branch": None, "reason": None, "plan": []},
            ],
        },
    }


class SupportHarness(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = make_server(0)
        cls.worker = threading.Thread(target=cls.server.serve_forever)
        cls.worker.start()

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.worker.join()
        cls.server.server_close()

    def post(self, request, route=ROUTE, **extra_headers):
        client = http.client.HTTPConnection("127.0.0.1", self.server.server_port, timeout=3)
        body = request if isinstance(request, bytes) else json.dumps(request).encode()
        try:
            client.request("POST", route, body,
                           {"Content-Type": "application/json", **extra_headers})
            response = client.getresponse()
            return response.status, json.load(response)
        finally:
            client.close()

    def test_direct_carries_a_legal_plan(self):
        data = fixture()
        status, reply = self.post(data)
        self.assertEqual(status, 200)
        self.assertEqual(reply["version"], data["version"])
        self.assertEqual(reply["condition"], data["condition"])
        self.assertEqual(reply["provider"], "mock")
        self.assertEqual(reply["recommended_plan"], data["context"]["provisional_plan"])
        self.assertIn("2 LP to West Fork", reply["message"])

    def test_dissent_has_no_recommendation(self):
        status, reply = self.post(fixture("constructive_dissent"))
        self.assertEqual(status, 200)
        self.assertEqual(reply["provider"], "mock")
        self.assertIsNone(reply["recommended_plan"])
        self.assertTrue(reply["message"].endswith("?"))

    def test_no_ai_is_neutral(self):
        status, reply = self.post(fixture("no_ai"))
        self.assertEqual(status, 200)
        self.assertEqual(reply["provider"], "none")
        self.assertIsNone(reply["recommended_plan"])

    def test_empty_budget_allows_empty_plan(self):
        data = fixture()
        data["context"]["points_available"] = 0
        data["context"]["provisional_plan"] = []
        for judgment in data["context"]["initial_judgments"]:
            judgment["plan"] = []
        status, reply = self.post(data)
        self.assertEqual(status, 200)
        self.assertEqual(reply["recommended_plan"], [])

    def test_rejects_unexpected_or_private_fields(self):
        for collection, field in (("sites", "hidden_difficulty"),
                                  ("initial_judgments", "participant_name"),
                                  ("initial_judgments", "explanation")):
            with self.subTest(field=field):
                data = fixture()
                data["context"][collection][0][field] = "must not be accepted"
                self.assertEqual(self.post(data), (400, {"error": "invalid_support_request"}))

    def test_rejects_invalid_plans_and_invalid_tree(self):
        for plan in (
            [{"target": "West Fork", "points": 3}, {"target": "East Fork", "points": 2}],
            [{"target": "Unknown Site", "points": 1}],
            [{"target": "Camp", "points": 1}],
            [{"target": "West Fork", "points": True}],
            [{"target": "West Fork", "points": 1}] * 2,
            [{"target": name, "points": 1} for name in ("West Fork", "Miner's Hut", "East Fork")],
        ):
            with self.subTest(plan=plan):
                data = fixture()
                data["context"]["provisional_plan"] = plan
                self.assertEqual(self.post(data)[0], 400)
        data = fixture()
        data["context"]["sites"][0]["parent"] = "Miner's Hut"
        self.assertEqual(self.post(data)[0], 400)

    def test_bad_request_and_network_guards(self):
        self.assertEqual(self.post(b"{")[0], 400)
        self.assertEqual(self.post(fixture(), route="/other")[0], 404)
        self.assertEqual(self.post(b" " * (MAX_BODY + 1))[0], 413)
        self.assertEqual(self.post(fixture(), Origin="https://example.com")[0], 403)
        self.assertEqual(self.post(fixture(), Host="example.com")[0], 403)
        self.assertEqual(self.post(fixture(), **{"Content-Type": "text/plain"})[0], 415)
        data = fixture()
        data["version"] = "unsupported"
        self.assertEqual(self.post(data)[0], 400)


if __name__ == "__main__":
    unittest.main()
