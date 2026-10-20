using System;
using System.Buffers.Binary;

namespace CaptionScribe.Services
{
    internal static class PcmConvert
    {
        public static int Pcm16BytesForSilence(int sampleRate, int channels, TimeSpan duration)
        {
            int frames = (int)(sampleRate * duration.TotalSeconds);
            if (frames < 0)
                frames = 0;
            return frames * channels * (PcmWavWriter.BitsPerSample / 8);
        }

        public static void WriteSilence(PcmWavWriter writer, TimeSpan duration)
        {
            int bytes = Pcm16BytesForSilence(writer.SampleRate, writer.Channels, duration);
            if (bytes == 0)
                return;
            var zeros = new byte[Math.Min(bytes, writer.BlockAlign * writer.SampleRate)];
            int left = bytes;
            while (left > 0)
            {
                int n = Math.Min(left, zeros.Length);
                writer.Write(zeros.AsSpan(0, n)); // cap: remaining silence is dropped
                left -= n;
            }
        }

        public static int MixToPcm16(ReadOnlySpan<byte> mix, int frames, int channels, bool isFloat, int bitsPerSample,
            Span<byte> pcm16)
        {
            int samples = frames * channels;
            int needed = samples * 2;
            if (pcm16.Length < needed)
                throw new ArgumentException("PCM buffer too small.", nameof(pcm16));

            if (isFloat && bitsPerSample == 32)
            {
                for (int i = 0; i < samples; i++)
                {
                    float f = BinaryPrimitives.ReadSingleLittleEndian(mix.Slice(i * 4, 4));
                    if (f > 1f) f = 1f;
                    if (f < -1f) f = -1f;
                    short s = (short)(f * 32767f);
                    BinaryPrimitives.WriteInt16LittleEndian(pcm16.Slice(i * 2, 2), s);
                }
                return needed;
            }

            if (!isFloat && bitsPerSample == 16)
            {
                mix.Slice(0, needed).CopyTo(pcm16);
                return needed;
            }

            if (!isFloat && bitsPerSample == 32)
            {
                for (int i = 0; i < samples; i++)
                {
                    int v = BinaryPrimitives.ReadInt32LittleEndian(mix.Slice(i * 4, 4));
                    BinaryPrimitives.WriteInt16LittleEndian(pcm16.Slice(i * 2, 2), (short)(v >> 16));
                }
                return needed;
            }

            throw new NotSupportedException("Unsupported mix format.");
        }

        public static float PeakPcm16(ReadOnlySpan<byte> pcm)
        {
            int max = 0;
            for (int i = 0; i + 1 < pcm.Length; i += 2)
            {
                int s = BinaryPrimitives.ReadInt16LittleEndian(pcm.Slice(i, 2));
                int a = s < 0 ? -s : s;
                if (a > max)
                    max = a;
            }
            return max / 32768f;
        }
    }
}
