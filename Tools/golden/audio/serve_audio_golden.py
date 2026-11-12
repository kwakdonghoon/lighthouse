# M6 오디오 골든 추출 서버: 127.0.0.1 전용. 사용법: python serve_audio_golden.py <port>
#  srv/ 를 정적으로 서빙하고, POST /put/<이름> 본문을 out/<이름> 으로 저장한다(.gz 로 끝나지 않는 *.pcm 은 gzip 으로 압축해 .gz 를 붙인다).
import http.server, os, sys, gzip
BASE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(BASE, 'srv'); OUT = os.path.join(BASE, 'out')
class H(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=ROOT, **k)
    def do_POST(self):
        n = int(self.headers.get('Content-Length', 0)); body = self.rfile.read(n)
        name = os.path.basename(self.path.split('?')[0])
        if name.endswith('.pcm'):
            name += '.gz'; body = gzip.compress(body, 9)
        with open(os.path.join(OUT, name), 'wb') as f: f.write(body)
        self.send_response(200); self.end_headers(); self.wfile.write(b'ok')
    def end_headers(self):
        self.send_header('Cache-Control', 'no-store'); super().end_headers()
    def log_message(self, *a): pass
http.server.ThreadingHTTPServer(('127.0.0.1', int(sys.argv[1])), H).serve_forever()
