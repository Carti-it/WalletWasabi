using Avalonia.Threading;
using NBitcoin;
using System.Reactive.Linq;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using FlashCap;
using FlashCap.Devices;
using SkiaSharp;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Logging;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.SkiaSharp;
using System.Reactive.Disposables;
using System.Collections.Generic;

namespace WalletWasabi.Fluent.ViewModels.Dialogs;

[NavigationMetaData(Title = "Camera", NavigationTarget = NavigationTarget.CompactDialogScreen)]
public partial class ShowQrCameraDialogViewModel : DialogViewModelBase<string?>
{
    private readonly Network _network;
    private readonly QRCodeReader _decoder = new();

    [AutoNotify] private SKBitmap? _qrImage;
    [AutoNotify] private string _errorMessage = "";
    [AutoNotify] private string _qrContent = "";

    private CancellationTokenSource? _cts;

    private static readonly Lazy<IReadOnlyList<MyCaptureDeviceDescriptor>> AvailableDevicesAndCharacteristics = new(() =>
        {
			return Dispatcher.UIThread.Invoke<IReadOnlyList<MyCaptureDeviceDescriptor>>(() => {
				try 
				{
					Console.WriteLine("Get capture devices (one-time)");
					var devices = new CaptureDevices();

					Console.WriteLine("Enumerate descriptors (one-time)");
					var devicesAndCharacteristics = devices
						.EnumerateDescriptors()
						.Where(d => d.Characteristics.Length >= 1)
						.Where(static d => d is not VideoForWindowsDeviceDescriptor)
						.SelectMany(static d => d.Characteristics, static (d, c) => new MyCaptureDeviceDescriptor(d, c))
						.ToArray();

					foreach (var item in devicesAndCharacteristics)
					{
						Logger.LogTrace($"Cached device: {item.Descriptor.Name} with characteristic: {item.Characteristic}");
					}

					return devicesAndCharacteristics;
				}
				catch (Exception ex)
				{
					Logger.LogError("Failed to enumerate capture devices (one-time): " + ex.Message);
					return Array.Empty<MyCaptureDeviceDescriptor>().AsReadOnly();
				}
			});
        }, LazyThreadSafetyMode.ExecutionAndPublication);

	public static CaptureDeviceDescriptor? Device { get; set; }

	// Constructed capture device.
    private static CaptureDevice? CaptureDevice;

    private static Action<SKBitmap?> UpdateQrImageFn = (bitmap) => { };

	public ShowQrCameraDialogViewModel(UiContext context, Network network)
    {
        _network = network;

        SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: true);
        UiContext = context;
    }

    protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
    {
        base.OnNavigatedTo(isInHistory, disposables);

        _cts = new CancellationTokenSource();
		UpdateQrImageFn = (bitmap) => { QrImage = bitmap; };

		_ = RunCameraLoopAsync(_cts.Token); // fire-and-forget
    }

    protected override void OnNavigatedFrom(bool isInHistory)
    {
		UpdateQrImageFn = (bitmap) => { };

		// Fire and forget.
		_ = Dispatcher.UIThread.InvokeAsync(async () => 
		{
			if (CaptureDevice is not null) 
			{
				Logger.LogDebug("Stopping...");
				await CaptureDevice.StopAsync();
				Logger.LogDebug("Stopped");

				await CaptureDevice.DisposeAsync();
				Logger.LogDebug("Disposed");

				CaptureDevice = null;
			}
		});

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        base.OnNavigatedFrom(isInHistory);
    }

    private async Task RunCameraLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
			await Dispatcher.UIThread.InvokeAsync(async () =>
			{
				if (CaptureDevice is null) 
				{
					// Access the lazy field → enumeration happens at most once in the app lifetime
					var devicesAndCharacteristics = AvailableDevicesAndCharacteristics.Value;

					if (devicesAndCharacteristics.Count == 0)
					{
						throw new InvalidOperationException("No camera devices available.");
					}

					// Pick the first available (you could add UI to select if multiple)
					var selected = devicesAndCharacteristics[0];

					if (selected.Characteristic.PixelFormat == PixelFormats.Unknown)
					{
						throw new InvalidOperationException("Unknown pixel format.");
					}

					Device = selected.Descriptor;

					Console.WriteLine("OpenAsync");
					CaptureDevice = await Device!.OpenAsync(
						selected.Characteristic,
						ct: cancellationToken,
						pixelBufferArrived: OnPixelBufferArrivedAsync
					);
				}

            	Logger.LogDebug("Starting");
            	await CaptureDevice!.StartAsync(cancellationToken);
				Logger.LogDebug("Started");
			});
        }
        catch (OperationCanceledException)
        {
			UpdateQrImageFn = (bitmap) => { };
			Logger.LogDebug("OperationCanceledException");
        }
        catch (Exception ex)
        {
            Logger.LogDebug($"Camera error: {ex.Message}");

            Dispatcher.UIThread.Post(async () =>
            {
                Close();
                await ShowErrorAsync(Title, ex.Message, "Something went wrong", NavigationTarget.CompactDialogScreen);
            });
        } 
    }


    private static async Task OnPixelBufferArrivedAsync(PixelBufferScope bufferScope)
    {
		Logger.LogDebug("OnPixelBufferArrivedAsync");
        ////////////////////////////////////////////////
        // Pixel buffer has arrived.
        // NOTE: Perhaps this thread context is NOT UI thread.
        ArraySegment<byte> image = bufferScope.Buffer.ReferImage();

		// Decode image data to a bitmap:
		SKBitmap bitmap = SKBitmap.Decode(image);

        // Capture statistics variables.
        var frameIndex = bufferScope.Buffer.FrameIndex;
        var timestamp = bufferScope.Buffer.Timestamp;

		// `bitmap` is copied, so we can release pixel buffer now.
        bufferScope.ReleaseNow();

		Dispatcher.UIThread.Invoke(() =>
		{
			UpdateQrImageFn(bitmap);

			// if (!string.IsNullOrEmpty(decoded))
			// {
			// 	var parseResult = AddressParser.Parse(decoded, _network);

			// 	if (parseResult.IsOk)
			// 	{
			// 		Close(DialogResultKind.Normal, decoded);
			// 	}
			// 	else
			// 	{
			// 		ErrorMessage = parseResult.Error ?? "Invalid address";
			// 		QrContent = decoded;
			// 	}
			// }
		});
    }

    private string Decode(PixelBufferScope scope)
    {
        using var bitmap = SKBitmap.Decode(scope.Buffer.ReferImage());
        var source = new SKBitmapLuminanceSource(bitmap);
        var binary = new BinaryBitmap(new HybridBinarizer(source));
        return _decoder.decode(binary)?.Text ?? "";
    }

    // Helper record to hold what we need (adjust based on actual types in FlashCap)
    private record MyCaptureDeviceDescriptor(CaptureDeviceDescriptor Descriptor, VideoCharacteristics Characteristic);
}
