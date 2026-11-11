using System;
using System.IO;
using System.IO.Compression;

namespace Lighthouse.Render.Verify
{
    /// <summary>픽셀 배열(0xAABBGGRR, 위 행부터)을 PNG 파일로 저장하는 작은 도구 (시험·확인용, 외부 라이브러리 없이 dotnet 과 Unity 양쪽에서 쓴다). 정수 배율 확대 옵션.</summary>
    public static class PngLite
    {
        public static void Save(string path, uint[] px, int w, int h, int scale = 1)
        {
            int W = w * scale, H = h * scale; var raw = new byte[H * (1 + W * 4)];
            for (int y = 0; y < H; y++)
            {
                int o = y * (1 + W * 4); raw[o++] = 0;
                for (int x = 0; x < W; x++) { uint p = px[(y / scale) * w + x / scale]; raw[o++] = (byte)p; raw[o++] = (byte)(p >> 8); raw[o++] = (byte)(p >> 16); raw[o++] = (byte)(p >> 24); }
            }
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0x9C);
                using (var ds = new DeflateStream(ms, CompressionMode.Compress, true)) ds.Write(raw, 0, raw.Length);
                uint a = 1, b = 0; foreach (var v in raw) { a = (a + v) % 65521; b = (b + a) % 65521; }
                uint ad = (b << 16) | a; ms.WriteByte((byte)(ad >> 24)); ms.WriteByte((byte)(ad >> 16)); ms.WriteByte((byte)(ad >> 8)); ms.WriteByte((byte)ad);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                using (var fs = File.Create(path))
                {
                    fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                    var ihdr = new byte[13]; Be(ihdr, 0, (uint)W); Be(ihdr, 4, (uint)H); ihdr[8] = 8; ihdr[9] = 6;
                    Chunk(fs, "IHDR", ihdr); Chunk(fs, "IDAT", ms.ToArray()); Chunk(fs, "IEND", new byte[0]);
                }
            }
        }

        static void Be(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; Be(len, 0, (uint)data.Length); s.Write(len, 0, 4);
            var tb = new byte[4 + data.Length]; for (int i = 0; i < 4; i++) tb[i] = (byte)type[i]; Array.Copy(data, 0, tb, 4, data.Length);
            s.Write(tb, 0, tb.Length);
            var crc = new byte[4]; Be(crc, 0, Crc32(tb)); s.Write(crc, 0, 4);
        }

        static uint[] table;
        static uint Crc32(byte[] d)
        {
            if (table == null) { table = new uint[256]; for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; table[n] = c; } }
            uint r = 0xFFFFFFFFu; foreach (var v in d) r = table[(r ^ v) & 255] ^ (r >> 8); return r ^ 0xFFFFFFFFu;
        }
    }
}
