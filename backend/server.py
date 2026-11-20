"""Loopback-only development service. Mock by default; no local records."""

import argparse
import json
from http.server import BaseHTTPRequestHandler, HTTPServer

from .openai_provider import OpenAIProvider, ProviderFailure
from .support import NO_AI, build_mock_response


ROUTE = "/v1/support"
MAX_BODY = 16_384


class Handler(BaseHTTPRequestHandler):
    timeout = 5

    def log_message(self, *args):
        pass

    def reply(self, status, body):
        encoded = json.dumps(body).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(encoded)

    def do_POST(self):
        if self.path != ROUTE:
            return self.reply(404, {"error": "not_found"})
        if (self.headers.get("Origin") is not None or
                self.headers.get("Host") not in (
                    f"127.0.0.1:{self.server.server_port}", f"localhost:{self.server.server_port}")):
            return self.reply(403, {"error": "local_harness_only"})
        if self.headers.get_content_type() != "application/json":
            return self.reply(415, {"error": "expected_json"})
        lengths = self.headers.get_all("Content-Length", [])
        if (len(lengths) != 1 or not lengths[0].isascii() or not lengths[0].isdigit()
                or self.headers.get("Transfer-Encoding") is not None):
            return self.reply(400, {"error": "invalid_content_length"})
        if len(lengths[0]) > 5 or int(lengths[0]) > MAX_BODY:
            return self.reply(413, {"error": "body_too_large"})
        try:
            size = int(lengths[0])
            body = self.rfile.read(size)
            if len(body) != size:
                return self.reply(400, {"error": "incomplete_body"})
            request = json.loads(body.decode("utf-8"))
            provider = self.server.support_provider
            if provider is None or (type(request) is dict and request.get("condition") == NO_AI):
                result = build_mock_response(request)
            else:
                result = provider.generate(request)
        except ProviderFailure as exc:
            status = 504 if str(exc) == "provider_timeout" else 502
            return self.reply(status, {"error": str(exc)})
        except (ValueError, RecursionError):
            return self.reply(400, {"error": "invalid_support_request"})
        except TimeoutError:
            return self.reply(408, {"error": "request_timeout"})
        self.reply(200, result)


def make_server(port, provider=None):
    server = HTTPServer(("127.0.0.1", port), Handler)
    server.support_provider = provider
    return server


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=8787)
    parser.add_argument("--provider", choices=("mock", "openai"), default="mock")
    args = parser.parse_args()
    try:
        provider = OpenAIProvider.from_environment() if args.provider == "openai" else None
    except ValueError as exc:
        parser.error(str(exc))
    with make_server(args.port, provider) as server:
        print(f"CLSR {args.provider} listening at http://127.0.0.1:{server.server_port}{ROUTE}", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass
