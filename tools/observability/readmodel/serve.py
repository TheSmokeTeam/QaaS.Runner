"""Serve an atomically materialized KPI snapshot over loopback (opt-in local use)."""
import argparse
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

from readmodel import open_limited

MAX_METRICS=2*1024*1024


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--metrics",type=Path,required=True)
    p.add_argument("--port",type=int,default=19333)
    a=p.parse_args()
    if not 1024 <= a.port <= 65535:
        p.error("port must be 1024–65535")
    class Handler(BaseHTTPRequestHandler):
        timeout=2
        def log_message(self,*_):
            pass
        def do_GET(self):
            if self.path!="/metrics":
                self.send_error(404)
                return
            try:
                with open_limited(a.metrics,MAX_METRICS) as f:
                    data=f.read(MAX_METRICS+1)
                if not data.startswith(b"# HELP qaas_runner_kpi_") or len(data)>MAX_METRICS:
                    raise ValueError
            except (OSError,ValueError):
                self.send_error(503,"no valid published metrics snapshot")
                return
            self.send_response(200)
            self.send_header("Content-Type","text/plain; version=0.0.4; charset=utf-8")
            self.send_header("Cache-Control","no-store")
            self.send_header("Content-Length",str(len(data)))
            self.end_headers()
            self.wfile.write(data)
    with HTTPServer(("127.0.0.1",a.port),Handler) as server:
        server.serve_forever()


if __name__=="__main__":
    main()
