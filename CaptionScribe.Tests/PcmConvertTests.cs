using System;
using CaptionScribe.Services;
using Xunit;

namespace CaptionScribe.Tests
{
    public class PcmConvertTests
    {
        [Fact]
        public void Pcm16BytesForSilence_MatchesRateAndDuration()
        {
            Assert.Equal(12_000, PcmConvert.Pcm16BytesForSilence(8000, 1, TimeSpan.FromMilliseconds(750)));
        }

        [Fact]
        public void MixToPcm16_CopiesPcm16()
        {
            byte[] mix = { 0x00, 0x01, 0xFF, 0x7F };
            var pcm = new byte[4];
            int n = PcmConvert.MixToPcm16(mix, frames: 2, channels: 1, isFloat: false, bitsPerSample: 16, pcm);
            Assert.Equal(4, n);
            Assert.Equal(mix, pcm);
        }

        [Fact]
        public void MixToPcm16_ConvertsFloat32()
        {
            var mix = new byte[8];
            BitConverter.GetBytes(1f).CopyTo(mix, 0);
            BitConverter.GetBytes(-1f).CopyTo(mix, 4);
            var pcm = new byte[4];
            PcmConvert.MixToPcm16(mix, frames: 2, channels: 1, isFloat: true, bitsPerSample: 32, pcm);
            Assert.Equal(32767, BitConverter.ToInt16(pcm, 0));
            Assert.Equal(-32767, BitConverter.ToInt16(pcm, 2));
        }

        [Fact]
        public void PeakPcm16_UsesMaxAbsoluteSample()
        {
            var pcm = new byte[4];
            BitConverter.GetBytes((short)100).CopyTo(pcm, 0);
            BitConverter.GetBytes((short)-16384).CopyTo(pcm, 2);
            Assert.Equal(0.5f, PcmConvert.PeakPcm16(pcm), 3);
        }
    }
}
