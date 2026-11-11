import http.server, os, sys
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'srv')
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'out')
class H(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=ROOT, **k)
    def do_POST(self):
        n = int(self.headers.get('Content-Length', 0))
        body = self.rfile.read(n)
        name = os.path.basename(self.path.split('?')[0])
        with open(os.path.join(OUT, name), 'wb') as f:
            f.write(body)
        self.send_response(200); self.end_headers(); self.wfile.write(b'ok')
    def log_message(self, *a):
        pass
http.server.ThreadingHTTPServer(('127.0.0.1', 47291), H).serve_forever()
