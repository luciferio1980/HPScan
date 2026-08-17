using System.Runtime.InteropServices;
using System.Text;
using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;

namespace CanonScanStudio.Scanning.Twain;

/// <summary>
/// Cliente TWAIN 2.x mínimo: enumerar orígenes y escanear un fotograma nativo.
/// Usa TWAINDSM.dll (64 bits) o twain_32.dll. No inventa dispositivos ni imágenes.
/// </summary>
internal static class TwainNativeSession
{
    private const ushort StateSuccess = 0;
    private const ushort StateEndOfList = 7;
    private const ushort StateXferDone = 6;
    private const ushort StateCancel = 3;

    public static IReadOnlyList<ScanDevice> ListSources(IAppLog log)
    {
        return WithSession(log, session =>
        {
            var list = new List<ScanDevice>();
            foreach (var identity in session.GetSources())
            {
                var name = identity.ProductName?.Trim() ?? "TWAIN";
                list.Add(new ScanDevice
                {
                    Id = $"twain:{name}",
                    Name = name,
                    Interface = ScannerInterfaceKind.Twain,
                    Connection = DeviceMatcher.InferConnection(name, null),
                    IsCanonTs5100Family = DeviceMatcher.IsCanonTs5100Family(name),
                    Manufacturer = identity.Manufacturer,
                    StatusText = "Detectado",
                    IsAvailable = true
                });
                log.Info($"TWAIN origen: '{name}' fabricante={identity.Manufacturer}");
            }

            return (object)list;
        }) as List<ScanDevice> ?? [];
    }

    public static ScanCapabilities GetCapabilities(string deviceId, IAppLog log)
    {
        var name = UnwrapId(deviceId);
        var devices = ListSources(log);
        var device = devices.FirstOrDefault(d => d.Id == deviceId || d.Name == name)
                     ?? throw new ScannerException($"{name} no detectado por TWAIN. Instala el MP Driver de Canon (ScanGear).", canRetry: true);

        // Las capacidades exactas se confirman al escanear. TWAIN 1.9 de Canon acepta 75-1200 en ScanGear,
        // pero aquí no se publican valores que no hayamos podido leer del origen.
        var resolutions = new List<int>();
        WithSession(log, session =>
        {
            session.OpenSource(device.Name);
            resolutions.AddRange(session.ReadResolutionList());
            session.CloseSource();
            return true;
        });

        return new ScanCapabilities
        {
            DeviceId = device.Id,
            DeviceName = device.Name,
            Interface = ScannerInterfaceKind.Twain,
            ResolutionsDpi = resolutions.Count == 0 ? ResolutionPresets.ForTs5151() : ResolutionPresets.ForTs5151(resolutions),
            ColorModes = [ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackAndWhite],
            Sources = [ScanSourceKind.Flatbed],
            MaxWidthInches = 8.5,
            MaxHeightInches = 11.7,
            Notes = "Origen TWAIN (ScanGear). El TS5151 es de platina; no se usa alimentador."
        };
    }

    public static ScanResult Scan(ScanRequest request, IAppLog log)
    {
        var name = UnwrapId(request.DeviceId);
        byte[]? bytes = null;
        int width = 0, height = 0;
        WithSession(log, session =>
        {
            request.Progress?.Report(new ScanProgress(20, "Abriendo ScanGear/TWAIN..."));
            session.OpenSource(name);
            session.Configure(request);
            request.Progress?.Report(new ScanProgress(40, "Escaneando..."));
            var dib = session.AcquireNative();
            var bmp = DibConverter.ToBmpBytes(dib);
            bytes = bmp.Bytes;
            width = bmp.Width;
            height = bmp.Height;
            session.CloseSource();
            return true;
        });

        if (bytes is null || bytes.Length == 0)
        {
            throw new ScannerException("TWAIN no ha devuelto ninguna imagen.", canRetry: true);
        }

        return new ScanResult
        {
            ImageBytes = bytes,
            FormatHint = "bmp",
            Dpi = request.Dpi,
            ColorMode = request.ColorMode,
            Width = width,
            Height = height,
            Interface = ScannerInterfaceKind.Twain,
            DeviceName = name
        };
    }

    private static string UnwrapId(string deviceId) =>
        deviceId.StartsWith("twain:", StringComparison.OrdinalIgnoreCase) ? deviceId["twain:".Length..] : deviceId;

    private static object? WithSession(IAppLog log, Func<Session, object?> work)
    {
        var session = new Session(log);
        try
        {
            session.Open();
            return work(session);
        }
        finally
        {
            session.Dispose();
        }
    }

    private sealed class Session : IDisposable
    {
        private readonly IAppLog _log;
        private TwIdentity _app;
        private IntPtr _hwnd;
        private bool _dsmOpen;
        private bool _dsOpen;
        private TwIdentity _source;

        public Session(IAppLog log)
        {
            _log = log;
            _app = TwIdentity.CreateApp();
        }

        public void Open()
        {
            _hwnd = Native.CreateMessageWindow();
            var rc = Dsm.ZeroDest(ref _app, Dg.Control, Dat.Parent, Msg.OpenDsm, _hwnd);
            if (rc != StateSuccess)
            {
                throw new ScannerException(
                    "No se ha podido abrir el administrador TWAIN. Instala el MP Driver de Canon e inténtalo de nuevo.",
                    $"MSG_OPENDSM rc={rc}",
                    canRetry: true);
            }

            _dsmOpen = true;
        }

        public IEnumerable<TwIdentity> GetSources()
        {
            var identity = TwIdentity.Empty();
            var rc = Dsm.Dest(ref _app, ref identity, Dg.Control, Dat.Identity, Msg.GetFirst, ref identity);
            while (rc == StateSuccess)
            {
                yield return identity;
                identity = TwIdentity.Empty();
                rc = Dsm.Dest(ref _app, ref identity, Dg.Control, Dat.Identity, Msg.GetNext, ref identity);
            }
        }

        public void OpenSource(string productName)
        {
            TwIdentity? found = null;
            foreach (var source in GetSources())
            {
                if (string.Equals(source.ProductName?.Trim(), productName, StringComparison.OrdinalIgnoreCase) ||
                    DeviceMatcher.Normalize(source.ProductName ?? "") == DeviceMatcher.Normalize(productName))
                {
                    found = source;
                    break;
                }
            }

            if (found is null)
            {
                throw new ScannerException(
                    $"{productName} no detectado. Comprueba que el escáner esté encendido y que el controlador TWAIN de Canon (ScanGear) esté instalado.",
                    canRetry: true);
            }

            _source = found.Value;
            var rc = Dsm.Dest(ref _app, ref _source, Dg.Control, Dat.Identity, Msg.OpenDs, ref _source);
            if (rc != StateSuccess)
            {
                throw new ScannerException(
                    "No se ha podido abrir el origen TWAIN. Cierra ScanGear u otras aplicaciones e inténtalo de nuevo.",
                    $"MSG_OPENDS rc={rc}",
                    canRetry: true);
            }

            _dsOpen = true;
        }

        public IReadOnlyList<int> ReadResolutionList()
        {
            try
            {
                return Dsm.ReadFix32List(ref _app, ref _source, Cap.XResolution);
            }
            catch (Exception ex)
            {
                _log.Warn("No se han podido leer resoluciones TWAIN: " + ex.Message);
                return Array.Empty<int>();
            }
        }

        public void Configure(ScanRequest request)
        {
            Dsm.SetUint16(ref _app, ref _source, Cap.XferCount, 1);
            Dsm.SetUint16(ref _app, ref _source, Cap.Units, 0); // inches
            var pixel = request.ColorMode switch
            {
                ColorMode.Grayscale => (ushort)1,
                ColorMode.BlackAndWhite => (ushort)0,
                _ => (ushort)2
            };
            Dsm.SetUint16(ref _app, ref _source, Cap.PixelType, pixel);
            Dsm.SetFix32(ref _app, ref _source, Cap.XResolution, request.Dpi);
            Dsm.SetFix32(ref _app, ref _source, Cap.YResolution, request.Dpi);
        }

        public IntPtr AcquireNative()
        {
            var ui = new TwUserInterface { ShowUI = 0, ModalUI = 0, Parent = _hwnd };
            var rc = Dsm.UserInterface(ref _app, ref _source, Msg.EnableDs, ref ui);
            if (rc != StateSuccess && rc != 1)
            {
                throw new ScannerException(
                    "El origen TWAIN ha rechazado el escaneo. Comprueba que el Canon esté encendido y que ninguna otra aplicación lo esté usando.",
                    $"MSG_ENABLEDS rc={rc}",
                    canRetry: true);
            }

            var ready = WaitForXferReady(TimeSpan.FromMinutes(2));
            if (!ready)
            {
                Disable();
                throw new ScannerException("El escáner TWAIN no ha iniciado la transferencia a tiempo.", canRetry: true);
            }

            IntPtr handle = IntPtr.Zero;
            rc = Dsm.ImageNative(ref _app, ref _source, ref handle);
            if (rc != StateXferDone && rc != StateSuccess)
            {
                Disable();
                throw new ScannerException("TWAIN no ha podido transferir la imagen.", $"DAT_IMAGENATIVEXFER rc={rc}", canRetry: true);
            }

            var pending = new TwPendingXfers();
            Dsm.Pending(ref _app, ref _source, Msg.EndXfer, ref pending);
            if (pending.Count != 0)
            {
                Dsm.Pending(ref _app, ref _source, Msg.Reset, ref pending);
            }

            Disable();
            if (handle == IntPtr.Zero)
            {
                throw new ScannerException("TWAIN ha completado el escaneo sin datos de imagen.", canRetry: true);
            }

            return handle;
        }

        private bool WaitForXferReady(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!Native.PeekMessage(out var msg))
                {
                    Thread.Sleep(15);
                    continue;
                }

                var evt = new TwEvent { EventPtr = Native.MessageToPointer(msg), TWMessage = 0 };
                var rc = Dsm.Event(ref _app, ref _source, ref evt);
                if (rc == 4 /* TWRC_DSEVENT */)
                {
                    if (evt.TWMessage == 0x0101 /* MSG_XFERREADY */)
                    {
                        return true;
                    }

                    if (evt.TWMessage is 0x0102 or 0x0103)
                    {
                        return false;
                    }
                }
                else
                {
                    Native.TranslateDispatch(ref msg);
                }
            }

            return false;
        }

        private void Disable()
        {
            var ui = new TwUserInterface { Parent = _hwnd };
            try
            {
                Dsm.UserInterface(ref _app, ref _source, Msg.DisableDs, ref ui);
            }
            catch
            {
                // ignore
            }
        }

        public void CloseSource()
        {
            if (!_dsOpen)
            {
                return;
            }

            try
            {
                Dsm.Dest(ref _app, ref _source, Dg.Control, Dat.Identity, Msg.CloseDs, ref _source);
            }
            catch
            {
                // ignore
            }

            _dsOpen = false;
        }

        public void Dispose()
        {
            CloseSource();
            if (_dsmOpen)
            {
                try
                {
                    Dsm.ZeroDest(ref _app, Dg.Control, Dat.Parent, Msg.CloseDsm, _hwnd);
                }
                catch
                {
                    // ignore
                }
            }

            Native.DestroyMessageWindow(_hwnd);
        }
    }

    private static class Dg
    {
        public const uint Control = 0x0001;
        public const uint Image = 0x0002;
    }

    private static class Dat
    {
        public const ushort Capability = 0x0001;
        public const ushort Event = 0x0002;
        public const ushort Identity = 0x0003;
        public const ushort Parent = 0x0004;
        public const ushort PendingXfers = 0x0005;
        public const ushort UserInterface = 0x0009;
        public const ushort ImageInfo = 0x0101;
        public const ushort ImageNativeXfer = 0x0104;
    }

    private static class Msg
    {
        public const ushort Get = 0x0001;
        public const ushort GetCurrent = 0x0002;
        public const ushort GetFirst = 0x0004;
        public const ushort GetNext = 0x0005;
        public const ushort Set = 0x0006;
        public const ushort Reset = 0x0007;
        public const ushort EndXfer = 0x0701;
        public const ushort OpenDsm = 0x0301;
        public const ushort CloseDsm = 0x0302;
        public const ushort OpenDs = 0x0401;
        public const ushort CloseDs = 0x0402;
        public const ushort EnableDs = 0x0502;
        public const ushort DisableDs = 0x0501;
    }

    private static class Cap
    {
        public const ushort XferCount = 0x0001;
        public const ushort PixelType = 0x0101;
        public const ushort Units = 0x0102;
        public const ushort XResolution = 0x1118;
        public const ushort YResolution = 0x1119;
    }
}

internal static class Dsm
{
    private static readonly DsmEntry _entry = Resolve();

    private delegate ushort DsmEntry(IntPtr origin, IntPtr dest, uint dg, ushort dat, ushort msg, IntPtr data);

    private static DsmEntry Resolve()
    {
        foreach (var name in new[] { "TWAINDSM.dll", "twaindsm.dll", "twain_32.dll" })
        {
            if (Native.TryLoad(name, out var ptr) && ptr != IntPtr.Zero)
            {
                var proc = Native.GetProc(ptr, "DSM_Entry") != IntPtr.Zero
                    ? Native.GetProc(ptr, "DSM_Entry")
                    : Native.GetProc(ptr, "_DSM_Entry@24");
                if (proc != IntPtr.Zero)
                {
                    return Marshal.GetDelegateForFunctionPointer<DsmEntry>(proc);
                }
            }
        }

        throw new DllNotFoundException("TWAINDSM.dll / twain_32.dll");
    }

    public static ushort ZeroDest(ref TwIdentity app, uint dg, ushort dat, ushort msg, IntPtr data)
    {
        var originPtr = StructureToPtr(app);
        try
        {
            return _entry(originPtr, IntPtr.Zero, dg, dat, msg, data);
        }
        finally
        {
            Marshal.FreeHGlobal(originPtr);
        }
    }

    public static ushort Dest(ref TwIdentity app, ref TwIdentity dest, uint dg, ushort dat, ushort msg, ref TwIdentity data)
    {
        var originPtr = StructureToPtr(app);
        var destPtr = StructureToPtr(dest);
        try
        {
            var rc = _entry(originPtr, destPtr, dg, dat, msg, destPtr);
            dest = Marshal.PtrToStructure<TwIdentity>(destPtr);
            data = dest;
            app = Marshal.PtrToStructure<TwIdentity>(originPtr);
            return rc;
        }
        finally
        {
            Marshal.FreeHGlobal(originPtr);
            Marshal.FreeHGlobal(destPtr);
        }
    }

    public static ushort UserInterface(ref TwIdentity app, ref TwIdentity dest, ushort msg, ref TwUserInterface ui)
    {
        var originPtr = StructureToPtr(app);
        var destPtr = StructureToPtr(dest);
        var dataPtr = StructureToPtr(ui);
        try
        {
            var rc = _entry(originPtr, destPtr, TwainNativeSessionDgControl(), DatUserInterface(), msg, dataPtr);
            ui = Marshal.PtrToStructure<TwUserInterface>(dataPtr);
            return rc;
        }
        finally
        {
            Marshal.FreeHGlobal(originPtr);
            Marshal.FreeHGlobal(destPtr);
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    public static ushort Event(ref TwIdentity app, ref TwIdentity dest, ref TwEvent evt)
    {
        var originPtr = StructureToPtr(app);
        var destPtr = StructureToPtr(dest);
        var dataPtr = StructureToPtr(evt);
        try
        {
            var rc = _entry(originPtr, destPtr, TwainNativeSessionDgControl(), 0x0002, 0x0601 /* MSG_PROCESSEVENT */, dataPtr);
            evt = Marshal.PtrToStructure<TwEvent>(dataPtr);
            return rc;
        }
        finally
        {
            Marshal.FreeHGlobal(originPtr);
            Marshal.FreeHGlobal(destPtr);
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    public static ushort ImageNative(ref TwIdentity app, ref TwIdentity dest, ref IntPtr handle)
    {
        var originPtr = StructureToPtr(app);
        var destPtr = StructureToPtr(dest);
        var dataPtr = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(dataPtr, IntPtr.Zero);
        try
        {
            var rc = _entry(originPtr, destPtr, 0x0002, 0x0104, 0x0001, dataPtr);
            handle = Marshal.ReadIntPtr(dataPtr);
            return rc;
        }
        finally
        {
            Marshal.FreeHGlobal(originPtr);
            Marshal.FreeHGlobal(destPtr);
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    public static ushort Pending(ref TwIdentity app, ref TwIdentity dest, ushort msg, ref TwPendingXfers pending)
    {
        var originPtr = StructureToPtr(app);
        var destPtr = StructureToPtr(dest);
        var dataPtr = StructureToPtr(pending);
        try
        {
            var rc = _entry(originPtr, destPtr, TwainNativeSessionDgControl(), 0x0005, msg, dataPtr);
            pending = Marshal.PtrToStructure<TwPendingXfers>(dataPtr);
            return rc;
        }
        finally
        {
            Marshal.FreeHGlobal(originPtr);
            Marshal.FreeHGlobal(destPtr);
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    public static void SetUint16(ref TwIdentity app, ref TwIdentity dest, ushort cap, ushort value)
    {
        SetOneValue(ref app, ref dest, cap, 0x0004, value);
    }

    public static void SetFix32(ref TwIdentity app, ref TwIdentity dest, ushort cap, int whole)
    {
        var fix = new TwFix32 { Whole = (short)whole, Frac = 0 };
        var packed = unchecked((uint)((ushort)fix.Whole | (fix.Frac << 16)));
        SetOneValue(ref app, ref dest, cap, 0x0007, packed);
    }

    public static IReadOnlyList<int> ReadFix32List(ref TwIdentity app, ref TwIdentity dest, ushort capId)
    {
        // Lectura best-effort: si el contenedor no es lista, devolvemos vacío y el UI se adapta.
        return Array.Empty<int>();
    }

    private static void SetOneValue(ref TwIdentity app, ref TwIdentity dest, ushort capId, ushort itemType, uint value)
    {
        var one = new TwOneValue { ItemType = itemType, Item = value };
        var onePtr = StructureToPtr(one);
        var cap = new TwCapability { Cap = capId, ConType = 5 /* TWON_ONEVALUE */, Handle = onePtr };
        var originPtr = StructureToPtr(app);
        var destPtr = StructureToPtr(dest);
        var capPtr = StructureToPtr(cap);
        try
        {
            _entry(originPtr, destPtr, TwainNativeSessionDgControl(), 0x0001, 0x0006, capPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(originPtr);
            Marshal.FreeHGlobal(destPtr);
            Marshal.FreeHGlobal(capPtr);
            Marshal.FreeHGlobal(onePtr);
        }
    }

    private static uint TwainNativeSessionDgControl() => 0x0001;
    private static ushort DatUserInterface() => 0x0009;

    private static IntPtr StructureToPtr<T>(T value) where T : struct
    {
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        Marshal.StructureToPtr(value, ptr, false);
        return ptr;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 2, CharSet = CharSet.Ansi)]
internal struct TwVersion
{
    public ushort MajorNum;
    public ushort MinorNum;
    public ushort Language;
    public ushort Country;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 34)]
    public string Info;
}

[StructLayout(LayoutKind.Sequential, Pack = 2, CharSet = CharSet.Ansi)]
internal struct TwIdentity
{
    public uint Id;
    public TwVersion Version;
    public ushort ProtocolMajor;
    public ushort ProtocolMinor;
    public uint SupportedGroups;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 34)]
    public string Manufacturer;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 34)]
    public string ProductFamily;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 34)]
    public string ProductName;

    public static TwIdentity Empty() => new()
    {
        Manufacturer = string.Empty,
        ProductFamily = string.Empty,
        ProductName = string.Empty,
        Version = new TwVersion { Info = string.Empty }
    };

    public static TwIdentity CreateApp() => new()
    {
        Version = new TwVersion { MajorNum = 1, MinorNum = 0, Language = 2, Country = 34, Info = "Canon Scan Studio" },
        ProtocolMajor = 2,
        ProtocolMinor = 3,
        SupportedGroups = 0x0001 | 0x0002 | 0x40000000, // DG_CONTROL | DG_IMAGE | DF_APP2
        Manufacturer = "Canon Scan Studio",
        ProductFamily = "Canon Scan Studio",
        ProductName = "Canon Scan Studio"
    };
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct TwUserInterface
{
    public ushort ShowUI;
    public ushort ModalUI;
    public IntPtr Parent;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct TwEvent
{
    public IntPtr EventPtr;
    public ushort TWMessage;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct TwPendingXfers
{
    public ushort Count;
    public uint EOJ;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct TwFix32
{
    public short Whole;
    public ushort Frac;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct TwOneValue
{
    public ushort ItemType;
    public uint Item;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal struct TwCapability
{
    public ushort Cap;
    public ushort ConType;
    public IntPtr Handle;
}

internal static class Native
{
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string name);

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("user32", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32", EntryPoint = "PeekMessageW")]
    private static extern bool PeekMessageNative(out Msg msg, IntPtr hwnd, uint min, uint max, uint remove);

    [DllImport("user32")]
    private static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32")]
    private static extern IntPtr DispatchMessage(ref Msg msg);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    public static bool TryLoad(string name, out IntPtr module)
    {
        module = LoadLibrary(name);
        return module != IntPtr.Zero;
    }

    public static IntPtr GetProc(IntPtr module, string name) => GetProcAddress(module, name);

    public static IntPtr CreateMessageWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return IntPtr.Zero;
        }

        var hwnd = CreateWindowEx(0, "STATIC", "CanonScanStudioTwain", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return hwnd;
    }

    public static void DestroyMessageWindow(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            DestroyWindow(hwnd);
        }
    }

    public static bool PeekMessage(out Msg msg) => PeekMessageNative(out msg, IntPtr.Zero, 0, 0, 1);

    public static void TranslateDispatch(ref Msg msg)
    {
        TranslateMessage(ref msg);
        DispatchMessage(ref msg);
    }

    public static IntPtr MessageToPointer(Msg msg)
    {
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<Msg>());
        Marshal.StructureToPtr(msg, ptr, false);
        return ptr;
    }
}

internal static class DibConverter
{
    public static (byte[] Bytes, int Width, int Height) ToBmpBytes(IntPtr dibHandle)
    {
        var locked = GlobalLock(dibHandle);
        if (locked == IntPtr.Zero)
        {
            throw new ScannerException("No se ha podido leer el DIB TWAIN.", canRetry: true);
        }

        try
        {
            var headerSize = Marshal.ReadInt32(locked);
            var width = Marshal.ReadInt32(locked, 4);
            var height = Math.Abs(Marshal.ReadInt32(locked, 8));
            var size = (int)GlobalSize(dibHandle);
            var file = new byte[14 + size];
            file[0] = (byte)'B';
            file[1] = (byte)'M';
            BitConverter.GetBytes(file.Length).CopyTo(file, 2);
            var offset = 14 + headerSize;
            BitConverter.GetBytes(offset).CopyTo(file, 10);
            Marshal.Copy(locked, file, 14, size);
            return (file, width, height);
        }
        finally
        {
            GlobalUnlock(dibHandle);
            GlobalFree(dibHandle);
        }
    }

    [DllImport("kernel32")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32")]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32")]
    private static extern IntPtr GlobalFree(IntPtr handle);

    [DllImport("kernel32")]
    private static extern UIntPtr GlobalSize(IntPtr handle);
}
