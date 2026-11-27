"""Run a synthetic Unity + mock backend check without opening the working project."""

import argparse
import base64
import json
import shutil
import subprocess
import tempfile
import threading
from pathlib import Path
from unittest.mock import patch

from backend.server import Handler, MOCK_ONLY_HEADER, make_server
from backend.support import validate_request


def check_snapshots(log):
    snapshots = {}
    for line in log.splitlines():
        if line.startswith("CLSR_SNAPSHOT "):
            _, label, data = line.split()
            request = json.loads(base64.b64decode(data))
            validate_request(request)
            snapshots[label] = request["context"]
    assert set(snapshots) == {"day1", "pending", "day2", "day3", "reset"}, snapshots.keys()
    day1, pending, day2, day3, reset = (
        snapshots[key] for key in ("day1", "pending", "day2", "day3", "reset")
    )

    def site(context, id):
        return next(site for site in context["sites"] if site["id"] == id)

    assert day1["points_available"] == 10 and day1["cumulative_costs"] and day1["released_route_reports"] == []
    assert site(day1, "R1")["initial_branch_report"] == 6
    assert site(day1, "C1")["initial_branch_report"] == 1
    assert site(day1, "W1")["initial_branch_report"] == 5
    assert site(day1, "W1")["initial_branch_note"] == "No cache. Trail crews expect the two outer approaches to differ in cost."
    assert site(day1, "N3")["available_cache_reward"] == 5
    assert site(day1, "W2")["known_branch_remaining"] is None
    assert site(day1, "W2")["known_difficulty"] is None
    assert site(day1, "C3")["known_remaining_here"] is None
    assert day1["initial_judgments"][2] == {"branch": None, "reason": None, "plan": []}
    assert pending["points_available"] == 9 and pending["released_route_reports"] == []
    assert day2["points_available"] == 9 and len(day2["released_route_reports"]) == 1
    assert "River Road: 6 → 4" in day2["released_route_reports"][0]
    assert "Cliff Pass: 1 → 7" in day2["released_route_reports"][0]
    assert site(day2, "W2")["known_difficulty"] == 1
    assert site(day2, "W2")["known_remaining_here"] is None
    assert site(day2, "W2")["known_branch_remaining"] == 5
    assert day3["points_available"] == 11 and site(day3, "N3")["available_cache_reward"] is None
    assert site(day3, "N3")["known_remaining_here"] == 0
    assert day3["released_route_reports"] == day2["released_route_reports"]
    assert site(day3, "C3")["known_difficulty"] is None
    assert reset["released_route_reports"] == [] and site(reset, "N3")["available_cache_reward"] == 5


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", required=True, help="Path to the Unity editor executable")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    calls = []
    do_post = Handler.do_POST

    def record(handler):
        calls.append(handler.headers.get(MOCK_ONLY_HEADER))
        return do_post(handler)

    class NeverLive:
        def generate(self, request):
            raise AssertionError("Unity review must not call a live provider")

    # The actual client uses port 8787. Fail rather than interrupt an existing server.
    with tempfile.TemporaryDirectory(prefix="clsr-stage3-") as folder, \
            patch.object(Handler, "do_POST", record), make_server(8787, provider=NeverLive()) as server:
        worker = threading.Thread(target=server.serve_forever, daemon=True)
        worker.start()
        copy = Path(folder)
        try:
            for name in ("Assets", "Packages", "ProjectSettings"):
                shutil.copytree(root / name, copy / name)
            editor = copy / "Assets" / "Editor"
            editor.mkdir(exist_ok=True)
            shutil.copyfile(Path(__file__).with_name("unity") / "ReviewSupportHarness.cs",
                            editor / "ReviewSupportHarness.cs")
            log_path = copy / "unity.log"
            try:
                result = subprocess.run([
                    args.unity, "-batchmode", "-nographics", "-projectPath", folder,
                    "-executeMethod", "ReviewSupportHarness.Run", "-logFile", str(log_path),
                ], timeout=150)
            except subprocess.TimeoutExpired:
                log = log_path.read_text(errors="replace") if log_path.exists() else ""
                if "Licensing initialization failed" in log:
                    raise SystemExit("Unity licensing did not start. Open the licensed Editor in Unity Hub and retry.") from None
                raise SystemExit("Unity did not finish the harness within 150 seconds.") from None
            log = log_path.read_text(errors="replace") if log_path.exists() else ""
            if result.returncode != 0 or "CLSR_SUPPORT_HARNESS_OK" not in log:
                lines = [line for line in log.splitlines()
                         if not any(term in line.lower() for term in
                                    ("token", "machine", "entitlement", "serial", "signature"))]
                raise SystemExit("\n".join(lines)[-9000:] or "Unity harness did not run.")
            check_snapshots(log)
            assert calls == ["1", "1", "1"], calls
            print("Unity mock harness: snapshots, both roles, no-AI bypass, failure and reading pause passed.")
        finally:
            server.shutdown()
            worker.join()


if __name__ == "__main__":
    main()
