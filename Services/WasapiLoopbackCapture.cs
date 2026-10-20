using System;
using System.Runtime.InteropServices;
using System.Threading;
using CaptionScribe.Core.Logging;

namespace CaptionScribe.Services
{
    /// <summary>WASAPI shared-mode loopback of the default render device. COM lives on the calling thread.</summary>
    internal sealed class WasapiLoopbackCapture : ILoopbackCapture
    {
        private const int ERender = 0;
        private const int ECapture = 1;
        private const int EConsole = 0;
        private const int ECommunications = 2;
        private const int ShareModeShared = 0;
        private const uint StreamFlagsLoopback = 0x00020000;
        private const int ActivationTypeProcessLoopback = 1;
        private const int ProcessLoopbackIncludeTree = 0;
        private const ushort VtBlob = 0x41;
        private const string ProcessLoopbackDevice = @"VAD\Process_Loopback";
        private const uint ClsctxAll = 0x17;
        private const long BufferDurationHns = 10_000_000;
        private const int WaveFormatPcm = 1;
        private const int WaveFormatIeeeFloat = 3;
        private const int WaveFormatTagExtensible = 0xFFFE;
        private const uint BufferFlagSilent = 0x1;
        private const uint AudclntSBufferEmpty = 0x08890001;

        private static readonly Guid MmDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
        private static readonly Guid AudioClientIid = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        private static readonly Guid AudioCaptureClientIid = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
        private static readonly Guid SubtypePcm = new("00000001-0000-0010-8000-00aa00389b71");
        private static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

        private readonly IAudioClient _client;
        private readonly IAudioCaptureClient _capture;
        private readonly IntPtr _mixFormat;
        private readonly int _blockAlign;
        private readonly int _maxPacketBytes;
        private bool _disposed;

        private WasapiLoopbackCapture(IAudioClient client, IAudioCaptureClient capture, IntPtr mixFormat,
            int sampleRate, int channels, bool isFloat, int bitsPerSample, int blockAlign, int maxPacketBytes)
        {
            _client = client;
            _capture = capture;
            _mixFormat = mixFormat;
            SampleRate = sampleRate;
            Channels = channels;
            IsFloat = isFloat;
            BitsPerSample = bitsPerSample;
            _blockAlign = blockAlign;
            _maxPacketBytes = Math.Max(blockAlign, maxPacketBytes);
        }

        public int SampleRate { get; }
        public int Channels { get; }
        public bool IsFloat { get; }
        public int BitsPerSample { get; }
        public int MaxPacketBytes => _maxPacketBytes;

        public static WasapiLoopbackCapture Open()
            => OpenEndpoint(ERender, EConsole, StreamFlagsLoopback);

        public static ILoopbackCapture OpenMeetingLoopback(uint teamsProcessId)
            => PreferProcess(teamsProcessId, OpenProcessLoopback, Open);

        internal static ILoopbackCapture PreferProcess(uint processId, Func<uint, ILoopbackCapture> openProcess,
            Func<ILoopbackCapture> openDevice)
        {
            if (processId != 0)
            {
                try { return openProcess(processId); }
                catch { /* device fallback */ }
            }
            return openDevice();
        }

        public static WasapiLoopbackCapture OpenProcessLoopback(uint processId)
        {
            var handler = new ActivateHandler();
            var activation = new AudioClientActivationParams
            {
                ActivationType = ActivationTypeProcessLoopback,
                TargetProcessId = processId,
                ProcessLoopbackMode = ProcessLoopbackIncludeTree,
            };
            int paramsSize = Marshal.SizeOf<AudioClientActivationParams>();
            IntPtr paramsPtr = Marshal.AllocHGlobal(paramsSize);
            int ptrOff = IntPtr.Size == 8 ? 16 : 12;
            IntPtr propPtr = Marshal.AllocHGlobal(ptrOff + IntPtr.Size);
            try
            {
                Marshal.StructureToPtr(activation, paramsPtr, false);
                for (int i = 0; i < ptrOff + IntPtr.Size; i++)
                    Marshal.WriteByte(propPtr, i, 0);
                Marshal.WriteInt16(propPtr, 0, (short)VtBlob);
                Marshal.WriteInt32(propPtr, 8, paramsSize);
                Marshal.WriteIntPtr(propPtr, ptrOff, paramsPtr);
                var iid = AudioClientIid;
                int hr = ActivateAudioInterfaceAsync(ProcessLoopbackDevice, ref iid, propPtr, handler, out var asyncOp);
                try
                {
                    Marshal.ThrowExceptionForHR(hr);
                    if (!handler.Done.Wait(TimeSpan.FromSeconds(5)))
                        throw new TimeoutException("Process loopback activation timed out.");
                    Marshal.ThrowExceptionForHR(handler.ActivateHr);
                    var client = AsAudioClient(handler.Client)
                        ?? throw new InvalidOperationException("Process loopback did not return IAudioClient.");
                    return FinishClient(client, streamFlags: 0);
                }
                finally
                {
                    if (asyncOp is not null)
                        SafeRelease(asyncOp);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(propPtr);
                Marshal.FreeHGlobal(paramsPtr);
            }
        }

        public static WasapiLoopbackCapture OpenMicrophone()
        {
            try { return OpenEndpoint(ECapture, ECommunications, streamFlags: 0); }
            catch (Exception)
            {
                return OpenEndpoint(ECapture, EConsole, streamFlags: 0);
            }
        }

        private static WasapiLoopbackCapture OpenEndpoint(int dataFlow, int role, uint streamFlags)
        {
            object? enumeratorObject = null;
            IMMDevice? device = null;
            object? clientObject = null;
            try
            {
                var enumeratorType = Type.GetTypeFromCLSID(MmDeviceEnumeratorClsid)
                    ?? throw new InvalidOperationException("WASAPI device enumerator is unavailable.");
                enumeratorObject = Activator.CreateInstance(enumeratorType)!;
                var enumerator = (IMMDeviceEnumerator)enumeratorObject;
                int hr = enumerator.GetDefaultAudioEndpoint(dataFlow, role, out device);
                Marshal.ThrowExceptionForHR(hr);
                var audioClientIid = AudioClientIid;
                hr = device.Activate(ref audioClientIid, ClsctxAll, IntPtr.Zero, out clientObject);
                Marshal.ThrowExceptionForHR(hr);
                var capture = FinishClient((IAudioClient)clientObject, streamFlags);
                clientObject = null;
                return capture;
            }
            finally
            {
                if (clientObject is not null)
                    SafeRelease(clientObject);
                if (device is not null)
                    SafeRelease(device);
                if (enumeratorObject is not null)
                    SafeRelease(enumeratorObject);
            }
        }

        private static WasapiLoopbackCapture FinishClient(IAudioClient client, uint streamFlags)
        {
            IntPtr mixFormat = IntPtr.Zero;
            object? captureObject = null;
            bool started = false;
            try
            {
                int hr = client.GetMixFormat(out mixFormat);
                Marshal.ThrowExceptionForHR(hr);
                ParseMixFormat(mixFormat, out int sampleRate, out int channels, out bool isFloat, out int bits, out int blockAlign);
                hr = client.Initialize(ShareModeShared, streamFlags, BufferDurationHns, 0, mixFormat, IntPtr.Zero);
                Marshal.ThrowExceptionForHR(hr);
                var captureIid = AudioCaptureClientIid;
                hr = client.GetService(ref captureIid, out captureObject);
                Marshal.ThrowExceptionForHR(hr);
                hr = client.Start();
                Marshal.ThrowExceptionForHR(hr);
                started = true;
                int packetBytes = blockAlign;
                if (client.GetBufferSize(out uint bufferFrames) >= 0 && bufferFrames > 0 && blockAlign > 0)
                    packetBytes = (int)Math.Min(int.MaxValue, (long)bufferFrames * blockAlign);
                var capture = new WasapiLoopbackCapture(client, (IAudioCaptureClient)captureObject, mixFormat,
                    sampleRate, channels, isFloat, bits, blockAlign, packetBytes);
                mixFormat = IntPtr.Zero;
                captureObject = null;
                started = false;
                return capture;
            }
            finally
            {
                if (started)
                {
                    try { client.Stop(); } catch { /* best-effort cleanup */ }
                }
                if (captureObject is not null)
                    SafeRelease(captureObject);
                if (mixFormat != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(mixFormat);
            }
        }

        private static IAudioClient? AsAudioClient(object? com)
        {
            if (com is IAudioClient typed)
                return typed;
            if (com is null)
                return null;
            IntPtr unk = Marshal.GetIUnknownForObject(com);
            try
            {
                var iid = AudioClientIid;
                int hr = Marshal.QueryInterface(unk, ref iid, out IntPtr pClient);
                if (hr < 0 || pClient == IntPtr.Zero)
                    return null;
                try
                {
                    return (IAudioClient)Marshal.GetObjectForIUnknown(pClient);
                }
                finally
                {
                    Marshal.Release(pClient);
                }
            }
            finally
            {
                Marshal.Release(unk);
            }
        }

        [DllImport("Mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int ActivateAudioInterfaceAsync(
            string deviceInterfacePath,
            ref Guid riid,
            IntPtr activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        public int Read(byte[] buffer, out int frames)
        {
            frames = 0;
            int hr = _capture.GetNextPacketSize(out uint packetFrames);
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
            if (packetFrames == 0)
                return 0;

            hr = _capture.GetBuffer(out IntPtr data, out uint got, out uint flags, out _, out _);
            if ((uint)hr == AudclntSBufferEmpty || got == 0)
            {
                if (hr >= 0)
                    _capture.ReleaseBuffer(got);
                return 0;
            }
            Marshal.ThrowExceptionForHR(hr);

            int bytes = (int)got * _blockAlign;
            if (_blockAlign <= 0 || bytes <= 0)
            {
                _capture.ReleaseBuffer(got);
                return 0;
            }
            if (bytes > buffer.Length)
            {
                _capture.ReleaseBuffer(got);
                throw new InvalidOperationException("Capture buffer is smaller than the WASAPI packet.");
            }
            frames = bytes / _blockAlign;
            if ((flags & BufferFlagSilent) != 0)
                Array.Clear(buffer, 0, bytes);
            else if (data != IntPtr.Zero && bytes > 0)
                Marshal.Copy(data, buffer, 0, bytes);
            _capture.ReleaseBuffer(got);
            return bytes;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            try { _client.Stop(); } catch { /* already stopped */ }
            if (_mixFormat != IntPtr.Zero)
                Marshal.FreeCoTaskMem(_mixFormat);
            SafeRelease(_capture);
            SafeRelease(_client);
        }

        internal static void ParseMixFormat(IntPtr format, out int sampleRate, out int channels, out bool isFloat,
            out int bitsPerSample, out int blockAlign)
        {
            if (format == IntPtr.Zero)
                throw new InvalidOperationException("WASAPI mix format was null.");
            var fmt = Marshal.PtrToStructure<WaveFormatEx>(format);
            sampleRate = (int)fmt.nSamplesPerSec;
            channels = fmt.nChannels;
            bitsPerSample = fmt.wBitsPerSample;
            blockAlign = fmt.nBlockAlign;
            isFloat = fmt.wFormatTag == WaveFormatIeeeFloat;
            if (fmt.wFormatTag == WaveFormatPcm)
                isFloat = false;
            else if (fmt.wFormatTag == WaveFormatTagExtensible)
            {
                var ext = Marshal.PtrToStructure<WaveFormatExtensible>(format);
                isFloat = ext.SubFormat == SubtypeIeeeFloat;
                if (ext.SubFormat == SubtypePcm)
                    isFloat = false;
            }
        }

        private static void SafeRelease(object com)
        {
            try { Marshal.ReleaseComObject(com); } catch { /* already released */ }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AudioClientActivationParams
        {
            public int ActivationType;
            public uint TargetProcessId;
            public int ProcessLoopbackMode;
        }

        [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceCompletionHandler
        {
            [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
        }

        [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IActivateAudioInterfaceAsyncOperation
        {
            [PreserveSig] int GetActivateResult(out int activateResult,
                [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
        }

        [ComVisible(true)]
        private sealed class ActivateHandler : IActivateAudioInterfaceCompletionHandler
        {
            public readonly ManualResetEventSlim Done = new(false);
            public int ActivateHr;
            public object? Client;

            public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
            {
                try
                {
                    activateOperation.GetActivateResult(out ActivateHr, out Client);
                }
                catch (Exception)
                {
                    ActivateHr = unchecked((int)0x80004005);
                }
                Done.Set();
                return 0;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WaveFormatEx
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WaveFormatExtensible
        {
            public WaveFormatEx Format;
            public ushort wValidBitsPerSample;
            public uint dwChannelMask;
            public Guid SubFormat;
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint clsctx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig] int OpenPropertyStore(int stgm, out IntPtr store);
            [PreserveSig] int GetId(out IntPtr id);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            [PreserveSig] int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration, long hnsPeriodicity,
                IntPtr format, IntPtr session);
            [PreserveSig] int GetBufferSize(out uint size);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint padding);
            [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr eventHandle);
            [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint numFrames, out uint flags, out ulong devicePosition,
                out ulong qpcPosition);
            [PreserveSig] int ReleaseBuffer(uint numFrames);
            [PreserveSig] int GetNextPacketSize(out uint numFrames);
        }
    }

    internal sealed class WasapiLoopbackFactory : ILoopbackCaptureFactory
    {
        private readonly Func<uint> _teamsProcessId;

        private readonly ILog? _log;

        public WasapiLoopbackFactory(Func<uint>? teamsProcessId = null, ILog? log = null)
        {
            _teamsProcessId = teamsProcessId ?? (() => 0);
            _log = log;
        }

        public ILoopbackCapture Start()
        {
            uint pid = _teamsProcessId();
            if (pid != 0)
            {
                try { return WasapiLoopbackCapture.OpenProcessLoopback(pid); }
                catch (Exception ex)
                {
                    _log?.Warning("Teams process loopback failed, using device playback: " + ex.Message);
                }
            }
            return WasapiLoopbackCapture.Open();
        }
    }

    internal sealed class WasapiMicFactory : ILoopbackCaptureFactory
    {
        public ILoopbackCapture Start() => WasapiLoopbackCapture.OpenMicrophone();
    }
}
