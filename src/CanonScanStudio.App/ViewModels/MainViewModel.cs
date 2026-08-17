using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CanonScanStudio.App.Services;
using CanonScanStudio.App.Views;
using CanonScanStudio.Infrastructure;
using CanonScanStudio.Models;
using CanonScanStudio.Scanning;
using CanonScanStudio.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CanonScanStudio.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IScannerService _scanner;
    private readonly ISessionService _session;
    private readonly IImageProcessingService _images;
    private readonly IFileExportService _export;
    private readonly IImportService _import;
    private readonly IOcrService _ocr;
    private readonly ISettingsService _settings;
    private readonly IUndoService _undo;
    private readonly IUiDialogService _dialogs;
    private readonly IAppLog _log;
    private CancellationTokenSource? _scanCts;
    private bool _updatingEdit;
    private bool _suppressDeviceChange;
    private bool _updatingCapabilities;

    public MainViewModel(
        IScannerService scanner,
        ISessionService session,
        IImageProcessingService images,
        IFileExportService export,
        IImportService import,
        IOcrService ocr,
        ISettingsService settings,
        IUndoService undo,
        IUiDialogService dialogs,
        IAppLog log)
    {
        _scanner = scanner;
        _session = session;
        _images = images;
        _export = export;
        _import = import;
        _ocr = ocr;
        _settings = settings;
        _undo = undo;
        _dialogs = dialogs;
        _log = log;
        _scanner.Changed += (_, _) =>
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            dispatcher.BeginInvoke(RefreshScannerState);
        };
        SelectedDpi = ScanSettingDefaults.ChooseDpi(ResolutionPresets.UntilDeviceReady, _settings.Current.DefaultDpi);
        SelectedColor = ScanSettingDefaults.ChooseColor(ColorModes, _settings.Current.DefaultColorMode);
        SelectedPageSize = PageSizeDefinition.Find(_settings.Current.DefaultPageSizeId);
        SelectedFormat = _settings.Current.DefaultFormat;
        SelectedDestination = _settings.Current.Destination;
        OcrEnabled = _settings.Current.OcrEnabled;
        SelectedOcrLanguage = _settings.Current.OcrLanguage;
        AutoExposure = _settings.Current.AutoExposure;
        CustomWidth = _settings.Current.CustomWidthInches;
        CustomHeight = _settings.Current.CustomHeightInches;
        ReloadPagesFromSession();
    }

    public ObservableCollection<PageItemViewModel> Pages { get; } = [];
    public ObservableCollection<ScanDevice> Devices { get; } = [];
    public ObservableCollection<int> Resolutions { get; } = new(ResolutionPresets.UntilDeviceReady);
    public ObservableCollection<ColorMode> ColorModes { get; } = [ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackAndWhite];
    public ObservableCollection<PageSizeDefinition> PageSizes { get; } = new(PageSizeDefinition.Presets);
    public ObservableCollection<OutputFormat> Formats { get; } = [OutputFormat.Pdf, OutputFormat.Jpeg, OutputFormat.Png, OutputFormat.Tiff];
    public ObservableCollection<SendToDestination> Destinations { get; } = [SendToDestination.LocalFolder, SendToDestination.Desktop, SendToDestination.Documents, SendToDestination.EmailPlaceholder];

    [ObservableProperty] private ScanDevice? selectedDevice;
    [ObservableProperty] private PageItemViewModel? selectedPage;
    [ObservableProperty] private int selectedDpi = ScanSettingDefaults.Dpi;
    [ObservableProperty] private ColorMode selectedColor = ScanSettingDefaults.Color;
    [ObservableProperty] private PageSizeDefinition selectedPageSize = PageSizeDefinition.A4;
    [ObservableProperty] private OutputFormat selectedFormat = OutputFormat.Pdf;
    [ObservableProperty] private SendToDestination selectedDestination = SendToDestination.LocalFolder;
    [ObservableProperty] private bool ocrEnabled;
    [ObservableProperty] private string selectedOcrLanguage = "spa";
    [ObservableProperty] private bool autoExposure;
    [ObservableProperty] private double customWidth = 8.27;
    [ObservableProperty] private double customHeight = 11.69;
    [ObservableProperty] private int brightness;
    [ObservableProperty] private int contrast;
    [ObservableProperty] private int gamma;
    [ObservableProperty] private int saturation;
    [ObservableProperty] private double deskewAngle;
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private int scanProgress;
    [ObservableProperty] private string scanProgressText = "";
    [ObservableProperty] private string statusText = "Buscando escáner...";
    [ObservableProperty] private string deviceStatusText = "No disponible";
    [ObservableProperty] private double zoom = ScanSettingDefaults.Zoom;
    [ObservableProperty] private bool cropMode;
    [ObservableProperty] private double cropX;
    [ObservableProperty] private double cropY;
    [ObservableProperty] private double cropWidth = 100;
    [ObservableProperty] private double cropHeight = 100;
    [ObservableProperty] private string errorBanner = "";
    [ObservableProperty] private bool showAdvanced;

    public string PageCounter => Pages.Count == 0 ? "0 / 0" : $"{(SelectedPage?.Page.Order ?? 0) + 1} / {Pages.Count}";
    public string ZoomLabel => $"{Math.Round(Zoom * 100)} %";
    public bool CanSave => Pages.Count > 0 && !IsScanning;
    public bool HasPages => Pages.Count > 0;
    public bool HasPreview => SelectedPage?.Preview is not null;
    public bool HasSelectedPage => SelectedPage is not null;
    public string AddPageLabel => Pages.Count == 0 ? "Escanear página" : "Añadir página";
    public bool CanOrganize => Pages.Count >= 1 && !IsScanning;
    public string ScannerLabel => SelectedDevice?.DisplayName ?? "Ningún escáner";
    public string ConnectionLabel => SelectedDevice?.Connection switch
    {
        ScannerConnectionKind.Usb => "USB",
        ScannerConnectionKind.Network => "Wi-Fi / red",
        _ => SelectedDevice is null ? "Sin dispositivo" : "Windows"
    };
    public bool IsCustomSize => SelectedPageSize.Id == "Custom";
    public IReadOnlyList<OcrLanguage> LanguageOptions => _ocr.Languages;

    public async Task InitializeAsync()
    {
        await RefreshDevicesAsync();
    }

    [RelayCommand]
    public async Task RefreshDevicesAsync()
    {
        StatusText = "Buscando escáneres...";
        await Task.Run(() => _scanner.RefreshDevices());
        RefreshScannerState();
        ErrorBanner = SelectedDevice is null
            ? "No se ha detectado el escáner. Ábrelo en Configuración (Selector de red Canon) y pulsa Actualizar."
            : "";
    }

    private bool CanStartScan() => !IsScanning;

    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task ScanAsync()
    {
        if (_scanner.SelectedDevice is null)
        {
            await RefreshDevicesAsync();
            if (_scanner.SelectedDevice is null)
            {
                ShowScannerError(new ScannerException(
                    "No hay escáner listo. En Configuración abre el Selector de red Canon, marca el TS5100 y pulsa Actualizar."));
                return;
            }
        }

        var size = SelectedPageSize.Id == "Custom"
            ? SelectedPageSize with { WidthInches = CustomWidth, HeightInches = CustomHeight }
            : SelectedPageSize;
        if (_scanner.Capabilities is { ResolutionsDpi.Count: > 0 } caps &&
            !caps.SupportsDpi(SelectedDpi))
        {
            var max = caps.ResolutionsDpi.Max();
            ShowScannerError(new ScannerException(
                $"Esta conexión no admite {SelectedDpi} DPI (máximo {max} DPI). Elige {max} DPI en Resolución."));
            return;
        }

        IsScanning = true;
        ScanProgress = 5;
        ScanProgressText = "Escaneando...";
        StatusText = "Escaneando...";
        _scanCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanProgress = p.Percent;
                ScanProgressText = p.Message;
                StatusText = p.Message;
            });
            var result = await _scanner.ScanAsync(new ScanRequest
            {
                DeviceId = _scanner.SelectedDevice.Id,
                Dpi = SelectedDpi,
                ColorMode = SelectedColor,
                PageSize = size,
                Progress = progress,
                CancellationToken = _scanCts.Token
            });

            var pngPath = Path.Combine(_session.SessionFolder, $"{Guid.NewGuid():N}.png");
            _images.SaveOriginal(result.ImageBytes, pngPath, result.Dpi);
            var info = _images.ReadInfo(pngPath);
            var actualDpi = ResolutionPresets.InferFromPixels(info.Width, size.WidthInches);
            if (actualDpi <= 0)
            {
                actualDpi = result.Dpi > 0 ? result.Dpi : SelectedDpi;
            }

            result = result with { Width = info.Width, Height = info.Height, Dpi = actualDpi };
            var page = _session.AddScannedPage(result, File.ReadAllBytes(pngPath), pngPath);
            var item = AddPageItem(page);
            SelectedPage = item;
            _undo.Execute(new DelegateCommand("Escanear", () => { }, () =>
            {
                _session.RemovePages([page.Id]);
                ReloadPagesFromSession();
            }));
            ErrorBanner = "";
            StatusText = $"Página {Pages.Count} añadida · {actualDpi} DPI";
        }
        catch (Exception ex)
        {
            _log.Error("Escaneo fallido.", ex);
            ShowScannerError(ex);
            StatusText = "Error de escaneo";
        }
        finally
        {
            IsScanning = false;
            ScanProgressText = "";
            ScanCommand.NotifyCanExecuteChanged();
            RefreshScannerState();
            NotifyUi();
        }
    }

    [RelayCommand]
    private void Import()
    {
        var picked = _dialogs.PickOpenFiles("Imágenes y PDF|*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.pdf");
        if (string.IsNullOrWhiteSpace(picked))
        {
            return;
        }

        foreach (var file in picked.Split('|'))
        {
            ImportPath(file);
        }
    }

    public void ImportPath(string path)
    {
        try
        {
            foreach (var image in _import.Import(path))
            {
                var dest = Path.Combine(_session.SessionFolder, $"{Guid.NewGuid():N}.png");
                File.WriteAllBytes(dest, image.Bytes);
                var info = _images.ReadInfo(dest);
                var page = _session.AddImportedPage(dest, image.Dpi, info.Width, info.Height);
                AddPageItem(page);
            }

            SelectedPage = Pages.LastOrDefault();
            NotifyUi();
        }
        catch (Exception ex)
        {
            _dialogs.Info("Importar", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void DeleteSelected()
    {
        if (SelectedPage is null)
        {
            return;
        }

        if (_settings.Current.ConfirmPageDelete &&
            !_dialogs.Confirm("Eliminar página", "¿Eliminar la página seleccionada? El original se conserva en la sesión hasta que se cree un documento nuevo."))
        {
            return;
        }

        var id = SelectedPage.Page.Id;
        var snapshot = _session.Current.Pages.ToList();
        _undo.Execute(new DelegateCommand("Eliminar",
            () =>
            {
                _session.RemovePages([id]);
                ReloadPagesFromSession();
            },
            () =>
            {
                _session.Current.Pages.Clear();
                _session.Current.Pages.AddRange(snapshot);
                _session.Current.Renumber();
                ReloadPagesFromSession();
            }));
    }

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void DuplicateSelected()
    {
        if (SelectedPage is null) return;
        var copy = _session.DuplicatePage(SelectedPage.Page.Id);
        ReloadPagesFromSession();
        SelectedPage = Pages.FirstOrDefault(p => p.Page.Id == copy.Id);
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedPage is null) return;
        var index = Pages.IndexOf(SelectedPage);
        if (index <= 0) return;
        _session.MovePage(index, index - 1);
        ReloadPagesFromSession(selectId: SelectedPage.Page.Id);
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedPage is null) return;
        var index = Pages.IndexOf(SelectedPage);
        if (index < 0 || index >= Pages.Count - 1) return;
        _session.MovePage(index, index + 1);
        ReloadPagesFromSession(selectId: SelectedPage.Page.Id);
    }

    public void Reorder(int from, int to)
    {
        _session.MovePage(from, to);
        ReloadPagesFromSession();
    }

    private bool CanOrganizePages() => CanOrganize;

    [RelayCommand(CanExecute = nameof(CanOrganizePages))]
    private void OrganizePages()
    {
        var window = new OrganizePagesWindow(Pages.ToList())
        {
            Owner = Application.Current.MainWindow
        };
        if (window.ShowDialog() != true || window.OrderedIds.Count == 0)
        {
            return;
        }

        var before = _session.Current.Pages.Select(p => p.Id).ToList();
        var after = window.OrderedIds.ToList();
        if (before.SequenceEqual(after))
        {
            return;
        }

        _undo.Execute(new DelegateCommand("Organizar",
            () =>
            {
                _session.ApplyOrder(after);
                ReloadPagesFromSession(SelectedPage?.Page.Id);
            },
            () =>
            {
                _session.ApplyOrder(before);
                ReloadPagesFromSession(SelectedPage?.Page.Id);
            }));
    }

    private bool CanEditPage() => SelectedPage is not null;

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void RotateLeft() => MutateEdit("Rotar izquierda", e => e.RotateLeft());

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void RotateRight() => MutateEdit("Rotar derecha", e => e.RotateRight());

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void Rotate180() => MutateEdit("Rotar", e => e.Rotate180());

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void FlipHorizontal() => MutateEdit("Voltear", e => e.FlipHorizontal = !e.FlipHorizontal);

    [RelayCommand(CanExecute = nameof(CanEditPage))]
    private void FlipVertical() => MutateEdit("Voltear", e => e.FlipVertical = !e.FlipVertical);

    [RelayCommand]
    private void Grayscale() => MutateEdit("Escala de grises", e => e.Filter = DocumentFilter.Grayscale);

    [RelayCommand]
    private void BlackAndWhite() => MutateEdit("Blanco y negro", e => e.Filter = DocumentFilter.BlackAndWhite);

    [RelayCommand]
    private void Invert() => MutateEdit("Invertir", e => e.Filter = DocumentFilter.Invert);

    [RelayCommand]
    private void Enhance() => MutateEdit("Mejorar", e => e.EnhanceDocument = !e.EnhanceDocument);

    [RelayCommand]
    private void RemoveBorders() => MutateEdit("Bordes", e => e.RemoveBorders = !e.RemoveBorders);

    [RelayCommand]
    private void ResetEdit() => MutateEdit("Restablecer", e =>
    {
        var empty = PageEditState.Identity();
        e.RotationDegrees = empty.RotationDegrees;
        e.FlipHorizontal = empty.FlipHorizontal;
        e.FlipVertical = empty.FlipVertical;
        e.Crop = null;
        e.DeskewAngle = 0;
        e.Brightness = 0;
        e.Contrast = 0;
        e.Gamma = 0;
        e.Saturation = 0;
        e.Filter = DocumentFilter.None;
        e.EnhanceDocument = false;
        e.RemoveBorders = false;
    });

    [RelayCommand]
    private void DetectDocument()
    {
        if (SelectedPage is null) return;
        var region = _images.DetectDocument(SelectedPage.Page.OriginalPath);
        CropMode = true;
        CropX = region.X;
        CropY = region.Y;
        CropWidth = region.Width;
        CropHeight = region.Height;
    }

    [RelayCommand]
    private void ApplyCrop()
    {
        MutateEdit("Recortar", e => e.Crop = new CropRegion(CropX, CropY, CropWidth, CropHeight));
        CropMode = false;
    }

    [RelayCommand]
    private void CancelCrop() => CropMode = false;

    public void OpenCropPage(PageItemViewModel? item = null)
    {
        item ??= SelectedPage;
        if (item is null)
        {
            return;
        }

        SelectedPage = item;
        try
        {
            var window = new CropPageWindow(item, _images)
            {
                Owner = Application.Current.MainWindow
            };
            if (window.ShowDialog() != true || window.NormalizedCrop is null)
            {
                return;
            }

            CommitBakedCrop(item, window.NormalizedCrop);
        }
        catch (Exception ex)
        {
            _log.Error("No se ha podido abrir el recorte.", ex);
            _dialogs.Info("Recortar", "No se ha podido abrir la ventana de recorte. " + ex.GetBaseException().Message);
        }
    }

    private void CommitBakedCrop(PageItemViewModel item, CropRegion normalized)
    {
        var visual = item.Page.Edit.Clone();
        visual.Crop = null;
        var full = _images.ApplyEdits(item.Page.OriginalPath, visual);
        var info = _images.ReadInfo(full);
        var crop = new CropRegion(
            normalized.X * info.Width,
            normalized.Y * info.Height,
            normalized.Width * info.Width,
            normalized.Height * info.Height).Clamp(info.Width, info.Height);
        var cropped = _images.CropBytes(full, crop);
        var dest = Path.Combine(_session.SessionFolder, $"{Guid.NewGuid():N}.png");
        File.WriteAllBytes(dest, cropped);
        var croppedInfo = _images.ReadInfo(dest);
        var previousPath = item.Page.OriginalPath;
        var previousEdit = item.Page.Edit.Clone();
        var previousWidth = item.Page.OriginalWidth;
        var previousHeight = item.Page.OriginalHeight;
        _undo.Execute(new DelegateCommand("Recortar",
            () =>
            {
                item.Page.OriginalPath = dest;
                item.Page.OriginalWidth = croppedInfo.Width;
                item.Page.OriginalHeight = croppedInfo.Height;
                item.Page.Edit = PageEditState.Identity();
                _session.Current.IsDirty = true;
                RefreshPageImages(item);
                _session.SaveRecovery();
            },
            () =>
            {
                item.Page.OriginalPath = previousPath;
                item.Page.OriginalWidth = previousWidth;
                item.Page.OriginalHeight = previousHeight;
                item.Page.Edit = previousEdit;
                _session.Current.IsDirty = true;
                RefreshPageImages(item);
                _session.SaveRecovery();
            }));
        NotifyUi();
    }

    [RelayCommand]
    private void AutoDeskew()
    {
        if (SelectedPage is null) return;
        var angle = _images.DetectSkew(SelectedPage.Page.OriginalPath);
        DeskewAngle = angle;
        MutateEdit("Enderezar", e => e.DeskewAngle = angle);
    }

    [RelayCommand]
    private void NewSession()
    {
        if (Pages.Count > 0 && !_dialogs.Confirm("Nueva sesión", "Se cerrará el documento actual. ¿Continuar?"))
        {
            return;
        }

        _session.NewSession();
        _undo.Clear();
        Pages.Clear();
        SelectedPage = null;
        NotifyUi();
    }

    [RelayCommand]
    private void Save() => SaveInternal(quick: false);

    [RelayCommand]
    private void SaveAs() => SaveInternal(quick: false, forceDialog: true);

    [RelayCommand]
    private void QuickSave() => SaveInternal(quick: true);

    [RelayCommand]
    private void Undo()
    {
        _undo.Undo();
        ReloadPagesFromSession(SelectedPage?.Page.Id);
        SyncEditFromPage();
    }

    [RelayCommand]
    private void Redo()
    {
        _undo.Redo();
        ReloadPagesFromSession(SelectedPage?.Page.Id);
        SyncEditFromPage();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings, _scanner, _log);
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
        RefreshScannerState();
    }

    [RelayCommand]
    private void OpenDiagnostics()
    {
        var window = new DiagnosticsWindow(_scanner, _log, ScanCommand, PickWindowsScannerCommand);
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
        RefreshScannerState();
    }

    [RelayCommand]
    private void OpenHelp()
    {
        var window = new HelpWindow();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    [RelayCommand]
    private void OpenCanonDriver() => CanonSetupHelper.OpenDriverPage();

    [RelayCommand]
    private void OpenWindowsPrinters() => CanonSetupHelper.OpenWindowsPrinters();

    [RelayCommand]
    private void OpenNetworkSelector()
    {
        if (CanonSetupHelper.IsNetworkSelectorRunning())
        {
            _dialogs.Info(
                "Selector de red Canon",
                "El Selector EX2 ya está abierto. Marca el TS5100 series (el de tu MAC), pulsa Aceptar y cierra esa ventana.\n\nDespués pulsa Reintentar aquí. No hace falta abrir el Selector otra vez.");
            return;
        }

        if (CanonSetupHelper.TryOpenNetworkSelector())
        {
            _dialogs.Info(
                "Selector de red Canon",
                "Marca el Canon TS5100 series y pulsa Aceptar (imprescindible).\n\nCuando se cierre el cuadro, pulsa Reintentar. El icono puede seguir en la bandeja: eso es normal.");
            return;
        }

        if (_dialogs.Confirm(
                "Selector de red Canon",
                "No está instalado el Selector de escáner de red de Canon (viene con el MP Driver de la serie TS5100).\n\n¿Abrir la página de descarga oficial?"))
        {
            CanonSetupHelper.OpenDriverPage();
        }
    }

    [RelayCommand]
    private void PickWindowsScanner()
    {
        try
        {
            var picked = _scanner.PickInteractively();
            RefreshScannerState();
            if (picked is null)
            {
                ErrorBanner =
                    "Windows no ha mostrado ningún escáner. En Configuración instala el MP Driver o abre el Selector de red Canon.";
                return;
            }

            ErrorBanner = "";
            StatusText = "Escáner seleccionado: " + picked.DisplayName;
        }
        catch (Exception ex)
        {
            ShowScannerError(ex is ScannerException scanner
                ? scanner
                : new ScannerException(
                    "Windows no ha podido abrir el selector de escáneres. Instala el MP Driver oficial de la serie TS5100.",
                    ex.ToString(),
                    canRetry: true,
                    inner: ex));
        }
    }

    [RelayCommand]
    private void Exit() => Application.Current.Shutdown();

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(4, Zoom + 0.1);

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(0.1, Zoom - 0.1);

    [RelayCommand]
    private void FitToWindow()
    {
        if (Application.Current?.MainWindow is MainWindow window)
        {
            window.FitPagesInView();
            return;
        }

        Zoom = ScanSettingDefaults.Zoom;
    }

    partial void OnSelectedDeviceChanged(ScanDevice? value)
    {
        if (_suppressDeviceChange || value is null)
        {
            return;
        }

        if (_scanner.SelectedDevice?.Id != value.Id)
        {
            _scanner.SelectDevice(value.Id);
        }

        ApplyCapabilities();
        NotifyUi();
    }

    partial void OnSelectedPageChanged(PageItemViewModel? value)
    {
        foreach (var page in Pages)
        {
            page.IsSelected = page == value;
        }

        SyncEditFromPage();
        NotifyUi();
    }

    partial void OnBrightnessChanged(int value) => CommitExposure();
    partial void OnContrastChanged(int value) => CommitExposure();
    partial void OnGammaChanged(int value) => CommitExposure();
    partial void OnSaturationChanged(int value) => CommitExposure();
    partial void OnDeskewAngleChanged(double value)
    {
        if (_updatingEdit || SelectedPage is null) return;
        MutateEdit("Enderezar", e => e.DeskewAngle = value);
    }

    partial void OnZoomChanged(double value) => OnPropertyChanged(nameof(ZoomLabel));

    partial void OnSelectedDpiChanged(int value)
    {
        if (_updatingCapabilities || value <= 0)
        {
            return;
        }

        _settings.Current.DefaultDpi = value;
    }

    partial void OnSelectedColorChanged(ColorMode value)
    {
        if (_updatingCapabilities)
        {
            return;
        }

        _settings.Current.DefaultColorMode = value;
    }

    partial void OnIsScanningChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        OrganizePagesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanOrganize));
    }
    partial void OnSelectedFormatChanged(OutputFormat value) => _settings.Current.DefaultFormat = value;
    partial void OnOcrEnabledChanged(bool value) => _settings.Current.OcrEnabled = value;
    partial void OnAutoExposureChanged(bool value)
    {
        _settings.Current.AutoExposure = value;
        if (value)
        {
            Brightness = 0;
            Contrast = 0;
            Gamma = 0;
        }
    }

    private void CommitExposure()
    {
        if (_updatingEdit || SelectedPage is null) return;
        MutateEdit("Exposición", e =>
        {
            e.Brightness = Brightness;
            e.Contrast = Contrast;
            e.Gamma = Gamma;
            e.Saturation = Saturation;
        });
    }

    private void MutateEdit(string name, Action<PageEditState> mutate)
    {
        var item = SelectedPage ?? Pages.LastOrDefault();
        if (item is null)
        {
            return;
        }

        if (!ReferenceEquals(SelectedPage, item))
        {
            SelectedPage = item;
        }

        var page = item.Page;
        var before = page.Edit.Clone();
        var after = page.Edit.Clone();
        mutate(after);
        _undo.Execute(new DelegateCommand(name,
            () => ApplyEditState(item, after),
            () => ApplyEditState(item, before)));
    }

    private void ApplyEditState(PageItemViewModel item, PageEditState state)
    {
        item.Page.Edit = state.Clone();
        try
        {
            RefreshPageImages(item);
        }
        catch (Exception ex)
        {
            _log.Error("No se ha podido aplicar la edición a la página.", ex);
            _dialogs.Info("Editar página", "No se ha podido girar o actualizar la página. " + ex.Message);
        }

        if (ReferenceEquals(SelectedPage, item))
        {
            SyncEditFromPage();
        }

        _session.SaveRecovery();
        OnPropertyChanged(nameof(SelectedPage));
        OnPropertyChanged(nameof(HasPreview));
        item.NotifyLabels();
    }

    private void SyncEditFromPage()
    {
        _updatingEdit = true;
        var edit = SelectedPage?.Page.Edit ?? PageEditState.Identity();
        Brightness = edit.Brightness;
        Contrast = edit.Contrast;
        Gamma = edit.Gamma;
        Saturation = edit.Saturation;
        DeskewAngle = edit.DeskewAngle;
        if (edit.Crop is { } crop)
        {
            CropX = crop.X;
            CropY = crop.Y;
            CropWidth = crop.Width;
            CropHeight = crop.Height;
        }
        _updatingEdit = false;
        NotifyUi();
    }

    private PageItemViewModel AddPageItem(ScanPage page)
    {
        var item = new PageItemViewModel(page);
        Pages.Add(item);
        RefreshPageImages(item);
        NotifyUi();
        return item;
    }

    private void ReloadPagesFromSession(Guid? selectId = null)
    {
        Pages.Clear();
        foreach (var page in _session.Current.Pages.OrderBy(p => p.Order))
        {
            var item = new PageItemViewModel(page);
            Pages.Add(item);
            RefreshPageImages(item);
        }

        SelectedPage = Pages.FirstOrDefault(p => p.Page.Id == selectId) ?? Pages.LastOrDefault();
        NotifyUi();
    }

    private void RefreshPageImages(PageItemViewModel item)
    {
        try
        {
            var thumb = _images.CreateThumbnail(item.Page.OriginalPath, item.Page.Edit);
            item.Thumbnail = ImageSourceFactory.FromBytes(thumb);
            var previewEdge = Zoom > 1.5 ? 2400 : 1600;
            var preview = _images.ApplyEdits(item.Page.OriginalPath, item.Page.Edit, previewEdge);
            item.Preview = ImageSourceFactory.FromBytes(preview);
            item.NotifyLabels();
        }
        catch (Exception ex)
        {
            _log.Warn("No se ha podido generar la vista previa: " + ex.Message);
            try
            {
                var fallback = _images.ApplyEdits(item.Page.OriginalPath, PageEditState.Identity());
                item.Preview = ImageSourceFactory.FromBytes(fallback);
                item.Thumbnail = item.Preview;
                item.NotifyLabels();
            }
            catch (Exception applyFallback)
            {
                _log.Warn("Tampoco se ha podido regenerar la imagen: " + applyFallback.Message);
                try
                {
                    var raw = File.ReadAllBytes(item.Page.OriginalPath);
                    item.Preview = ImageSourceFactory.FromBytes(raw);
                    item.Thumbnail = item.Preview;
                    item.NotifyLabels();
                }
                catch (Exception rawFallback)
                {
                    _log.Warn("Tampoco se ha podido mostrar el archivo original: " + rawFallback.Message);
                }
            }
        }
    }

    private void RefreshScannerState()
    {
        _suppressDeviceChange = true;
        Devices.Clear();
        foreach (var device in _scanner.Devices)
        {
            Devices.Add(device);
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Id == _scanner.SelectedDevice?.Id) ?? Devices.FirstOrDefault();
        _suppressDeviceChange = false;
        ApplyCapabilities();
        DeviceStatusText = _scanner.Status switch
        {
            ScannerAvailability.Ready => "Listo",
            ScannerAvailability.Scanning => "Escaneando...",
            ScannerAvailability.Busy => "Ocupado",
            ScannerAvailability.Offline => "Desconectado",
            ScannerAvailability.NotFound => "No disponible",
            _ => "Desconocido"
        };
        StatusText = SelectedDevice is null ? "Escáner no detectado" : DeviceStatusText;
        NotifyUi();
    }

    private void ApplyCapabilities()
    {
        _updatingCapabilities = true;
        try
        {
            var caps = _scanner.Capabilities;
            var dpiList = ResolutionPresets.MergeAdvertised(caps?.ResolutionsDpi);
            var dpiReplaced = ReplaceCollection(Resolutions, dpiList);
            var chosenDpi = ScanSettingDefaults.ChooseDpi(dpiList, SelectedDpi);
            ForceSelectDpi(chosenDpi, dpiReplaced);

            IReadOnlyList<ColorMode> colorList = caps?.ColorModes is { Count: > 0 }
                ? caps.ColorModes
                : [ColorMode.Color, ColorMode.Grayscale, ColorMode.BlackAndWhite];
            var colorReplaced = ReplaceCollection(ColorModes, colorList);
            var chosenColor = ScanSettingDefaults.ChooseColor(colorList, SelectedColor);
            ForceSelectColor(chosenColor, colorReplaced);
        }
        finally
        {
            _updatingCapabilities = false;
            if (SelectedDpi > 0)
            {
                _settings.Current.DefaultDpi = SelectedDpi;
            }

            _settings.Current.DefaultColorMode = SelectedColor;
        }
    }

    private void ForceSelectDpi(int dpi, bool collectionReplaced)
    {
        if (collectionReplaced && SelectedDpi == dpi)
        {
            var other = Resolutions.FirstOrDefault(d => d != dpi);
            if (other != 0)
            {
                SelectedDpi = other;
            }
        }

        SelectedDpi = dpi;
        OnPropertyChanged(nameof(SelectedDpi));
    }

    private void ForceSelectColor(ColorMode color, bool collectionReplaced)
    {
        if (collectionReplaced && SelectedColor == color)
        {
            var other = ColorModes.FirstOrDefault(m => m != color);
            if (other != color)
            {
                SelectedColor = other;
            }
        }

        SelectedColor = color;
        OnPropertyChanged(nameof(SelectedColor));
    }

    private static bool ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        if (target.Count == source.Count)
        {
            var same = true;
            for (var i = 0; i < source.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(target[i], source[i]))
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return false;
            }
        }

        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }

        return true;
    }

    private void SaveInternal(bool quick, bool forceDialog = false)
    {
        if (Pages.Count == 0)
        {
            _dialogs.Info("Guardar", "No hay páginas para guardar. Escanea o importa un documento.");
            return;
        }

        var folder = ResolveFolder();
        var name = $"Documento_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        var format = SelectedFormat;
        var searchable = OcrEnabled && format == OutputFormat.Pdf;
        if (!quick || forceDialog)
        {
            var dialog = new SaveDialogWindow(folder, name, format, Pages.Count > 1, searchable)
            {
                Owner = Application.Current.MainWindow
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            folder = dialog.Folder;
            name = dialog.FileName;
            format = dialog.Format;
            searchable = dialog.Searchable;
        }
        else
        {
            folder = Path.Combine(folder, DateTime.Now.ToString("yyyy"), DateTime.Now.ToString("MM"));
        }

        try
        {
            var exported = BuildExportedPages(searchable);
            var files = _export.Export(exported, new ExportOptions
            {
                DestinationFolder = folder,
                FileNameWithoutExtension = name,
                Format = format,
                SeparateImages = format != OutputFormat.Pdf && Pages.Count > 1,
                SearchablePdf = searchable,
                OcrLanguage = SelectedOcrLanguage
            });
            _session.Current.IsDirty = false;
            _dialogs.Info("Guardar", "Documento guardado:" + Environment.NewLine + string.Join(Environment.NewLine, files));
        }
        catch (Exception ex)
        {
            _log.Error("Error al guardar.", ex);
            _dialogs.Info("Guardar", "No se ha podido guardar el documento. " + ex.Message);
        }
    }

    private List<ExportedPage> BuildExportedPages(bool searchable)
    {
        var list = new List<ExportedPage>();
        foreach (var item in Pages)
        {
            var bytes = _images.ApplyEdits(item.Page.OriginalPath, item.Page.Edit);
            OcrPageResult? ocr = null;
            if (searchable)
            {
                try
                {
                    ocr = _ocr.Recognize(item.Page.OriginalPath, SelectedOcrLanguage) with { PageId = item.Page.Id };
                }
                catch (Exception ex)
                {
                    _log.Warn("OCR no disponible: " + ex.Message);
                }
            }

            var info = _images.ReadInfo(item.Page.OriginalPath);
            list.Add(new ExportedPage(bytes, item.Page.Dpi == 0 ? info.Dpi : item.Page.Dpi, info.Width, info.Height, ocr));
        }

        return list;
    }

    private string ResolveFolder()
    {
        return SelectedDestination switch
        {
            SendToDestination.Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            SendToDestination.Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            SendToDestination.EmailPlaceholder => _settings.Current.DefaultSaveFolder,
            _ => string.IsNullOrWhiteSpace(_settings.Current.DefaultSaveFolder) ? AppPaths.DefaultDocuments : _settings.Current.DefaultSaveFolder
        };
    }

    private void ShowScannerError(Exception ex)
    {
        var message = ex is ScannerException scanner
            ? scanner.UserMessage
            : "No se puede acceder al escáner. Comprueba que esté encendido y en la misma red.";
        ErrorBanner = message.Trim();
    }

    private void NotifyUi()
    {
        OnPropertyChanged(nameof(PageCounter));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasSelectedPage));
        OnPropertyChanged(nameof(AddPageLabel));
        OnPropertyChanged(nameof(CanOrganize));
        OnPropertyChanged(nameof(ScannerLabel));
        OnPropertyChanged(nameof(ConnectionLabel));
        OnPropertyChanged(nameof(IsCustomSize));
        OnPropertyChanged(nameof(ZoomLabel));
        RotateLeftCommand.NotifyCanExecuteChanged();
        RotateRightCommand.NotifyCanExecuteChanged();
        Rotate180Command.NotifyCanExecuteChanged();
        FlipHorizontalCommand.NotifyCanExecuteChanged();
        FlipVerticalCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        DuplicateSelectedCommand.NotifyCanExecuteChanged();
        OrganizePagesCommand.NotifyCanExecuteChanged();
        ScanCommand.NotifyCanExecuteChanged();
    }
}
