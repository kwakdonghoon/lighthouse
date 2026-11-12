using System;
using System.IO;

namespace Lighthouse.Audio
{
    /// <summary>WAV(16비트 PCM) 읽기·쓰기. 도구·시험 전용(런타임 재생은 WAV 를 쓰지 않는다).</summary>
    public static class WavIo
    {
        public static void Write(string path, float[] left, float[] right, int frames, int sampleRate)
        {
            using (var fs = File.Create(path)) Write(fs, left, right, frames, sampleRate);
        }

        public static void Write(Stream s, float[] left, float[] right, int frames, int sampleRate)
        {
            int ch = right == null ? 1 : 2, bytes = frames * ch * 2;
            var w = new BinaryWriter(s);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
            w.Write((short)1); w.Write((short)ch); w.Write(sampleRate); w.Write(sampleRate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
            for (int i = 0; i < frames; i++)
            {
                w.Write(ToI16(left[i])); if (ch == 2) w.Write(ToI16(right[i]));
            }
            w.Flush();
        }

        public static short ToI16(float v)
        {
            double x = Math.Round(v * 32767.0); if (x > 32767) x = 32767; if (x < -32768) x = -32768; return (short)x;
        }
    }
}
