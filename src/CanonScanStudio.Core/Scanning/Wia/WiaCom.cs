using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CanonScanStudio.Scanning.Wia;

/// <summary>
/// Acceso a WIA Automation (wiaaut.dll) por IDispatch, sin Interop.WIA.dll.
/// WIA es COM y debe usarse en un hilo STA.
/// </summary>
internal static class WiaCom
{
    private const BindingFlags Dispatch = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;

    public static object Create(string progId)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new ScannerException(
                "Windows Image Acquisition (WIA) no está disponible en este equipo. Canon Scan Studio necesita Windows 10 u 11 con el componente WIA instalado.",
                $"Plataforma actual: {Environment.OSVersion}",
                canRetry: false);
        }

        var type = Type.GetTypeFromProgID(progId, throwOnError: false);
        if (type is null)
        {
            throw new ScannerException(
                "Windows Image Acquisition (WIA) no está disponible en este equipo. Canon Scan Studio necesita Windows 10 u 11 con el componente WIA instalado.",
                $"ProgID {progId} no registrado.",
                canRetry: false);
        }

        return Activator.CreateInstance(type)
               ?? throw new ScannerException("No se ha podido inicializar WIA.", progId, canRetry: false);
    }

    public static object CreateDeviceManager() => Create("WIA.DeviceManager");

    public static object? Get(object target, string name)
    {
        try
        {
            return target.GetType().InvokeMember(name, Dispatch | BindingFlags.GetProperty, null, target, null);
        }
        catch (Exception ex)
        {
            throw Wrap(ex, $"No se ha podido leer la propiedad WIA '{name}'.");
        }
    }

    public static void Set(object target, string name, object? value)
    {
        try
        {
            target.GetType().InvokeMember(name, Dispatch | BindingFlags.SetProperty, null, target, [value!]);
        }
        catch (Exception ex)
        {
            throw Wrap(ex, $"No se ha podido asignar la propiedad WIA '{name}'.");
        }
    }

    public static object? Call(object target, string name, params object?[] args)
    {
        try
        {
            return target.GetType().InvokeMember(name, Dispatch | BindingFlags.InvokeMethod, null, target, args);
        }
        catch (Exception ex)
        {
            throw Wrap(ex, $"No se ha podido ejecutar '{name}' en WIA.");
        }
    }

    public static object Item(object collection, object index)
    {
        Exception? last = null;
        foreach (var member in new[] { "Item", "get_Item" })
        {
            try
            {
                var result = targetInvoke(collection, member, index);
                if (result is not null)
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw Wrap(last ?? new InvalidOperationException("Colección WIA vacía."), "No se ha podido acceder a un elemento WIA.");

        static object? targetInvoke(object target, string name, object index)
        {
            var flags = name.StartsWith("get_", StringComparison.OrdinalIgnoreCase)
                ? Dispatch | BindingFlags.InvokeMethod
                : Dispatch | BindingFlags.GetProperty | BindingFlags.InvokeMethod;
            return target.GetType().InvokeMember(name, flags, null, target, [index]);
        }
    }

    public static int Count(object collection)
    {
        var value = Get(collection, "Count");
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public static object? TryGetProperty(object properties, object idOrName)
    {
        try
        {
            return Item(properties, idOrName);
        }
        catch
        {
            return null;
        }
    }

    public static object? ReadValue(object properties, object idOrName)
    {
        var prop = TryGetProperty(properties, idOrName);
        return prop is null ? null : Get(prop, "Value");
    }

    public static string? ReadString(object properties, object idOrName) =>
        ReadValue(properties, idOrName)?.ToString();

    public static int? ReadInt(object properties, object idOrName)
    {
        var value = ReadValue(properties, idOrName);
        return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public static bool TryWriteValue(object properties, object idOrName, object value)
    {
        var prop = TryGetProperty(properties, idOrName);
        if (prop is null)
        {
            return false;
        }

        try
        {
            Set(prop, "Value", value);
            return true;
        }
        catch
        {
            try
            {
                Call(prop, "set_Value", value);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static IReadOnlyList<int> ReadNumericSubtypes(object properties, int propertyId)
    {
        var prop = TryGetProperty(properties, propertyId);
        if (prop is null)
        {
            return Array.Empty<int>();
        }

        var result = new List<int>();
        try
        {
            var subType = Convert.ToInt32(Get(prop, "SubType") ?? 0, CultureInfo.InvariantCulture);
            if ((subType & WiaConstants.PropList) != 0 || subType == 3)
            {
                if (Get(prop, "SubTypeValues") is { } vector)
                {
                    var count = Count(vector);
                    for (var i = 1; i <= count; i++)
                    {
                        try
                        {
                            var item = Item(vector, i);
                            result.Add(Convert.ToInt32(item, CultureInfo.InvariantCulture));
                        }
                        catch
                        {
                            // Ignora valores no numéricos.
                        }
                    }
                }
            }
            else if ((subType & WiaConstants.PropRange) != 0 || subType == 2)
            {
                var min = Convert.ToInt32(Get(prop, "SubTypeMin") ?? 0, CultureInfo.InvariantCulture);
                var max = Convert.ToInt32(Get(prop, "SubTypeMax") ?? min, CultureInfo.InvariantCulture);
                var step = Convert.ToInt32(Get(prop, "SubTypeStep") ?? 1, CultureInfo.InvariantCulture);
                if (step <= 0)
                {
                    step = 1;
                }

                foreach (var candidate in WiaConstants.PreferredResolutions)
                {
                    if (candidate >= min && candidate <= max && ((candidate - min) % step == 0 || step == 1))
                    {
                        result.Add(candidate);
                    }
                }

                if (result.Count == 0 && max >= min)
                {
                    result.Add(min);
                    if (max != min)
                    {
                        result.Add(max);
                    }
                }
            }
        }
        catch
        {
            // Algunos controladores no exponen SubType. En ese caso no inventamos capacidades.
        }

        return result.Distinct().OrderBy(v => v).ToArray();
    }

    public static byte[] ReadImageBytes(object imageFile)
    {
        var fileData = Get(imageFile, "FileData")
                       ?? throw new ScannerException("El escáner no ha devuelto datos de imagen.", canRetry: true);
        object? binary = null;
        try
        {
            binary = Get(fileData, "BinaryData");
        }
        catch
        {
            binary = Call(fileData, "get_BinaryData");
        }

        if (binary is byte[] bytes)
        {
            return bytes;
        }

        if (binary is not null)
        {
            return (byte[])binary;
        }

        throw new ScannerException("El escáner ha devuelto una imagen vacía.", canRetry: true);
    }

    public static void Release(object? comObject)
    {
        if (comObject is null || !OperatingSystem.IsWindows() || !Marshal.IsComObject(comObject))
        {
            return;
        }

        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch
        {
            // Liberación de COM no debe tumbar el escaneo ya completado.
        }
    }

    private static ScannerException Wrap(Exception ex, string userMessage)
    {
        if (ex is ScannerException scanner)
        {
            return scanner;
        }

        if (ex is TargetInvocationException { InnerException: { } inner })
        {
            return WiaErrorMapper.Map(inner);
        }

        return WiaErrorMapper.Map(ex);
    }
}
