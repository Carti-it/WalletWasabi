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
using Avalonia.Media.Imaging;
using FlashCap.Utilities;

namespace WalletWasabi.Fluent.ViewModels.Dialogs;

[NavigationMetaData(Title = "Camera", NavigationTarget = NavigationTarget.CompactDialogScreen)]
public partial class ShowQrCameraDialogViewModel : DialogViewModelBase<string?>
{
    private readonly Network _network;
    private readonly QRCodeReader _decoder = new();

    [AutoNotify] private Bitmap? _qrImage;
    [AutoNotify] private string _errorMessage = "";
    [AutoNotify] private string _qrContent = "";

    private static readonly Lazy<MyCaptureDeviceDescriptor> DeviceWrapper = new(() => Dispatcher.UIThread.Invoke(() =>
	{
		try
		{
			Console.WriteLine("Get capture devices (one-time)");
			var devices = new CaptureDevices();

			Console.WriteLine("Enumerate descriptors (one-time)");
			MyCaptureDeviceDescriptor[] devicesAndCharacteristics = devices
				.EnumerateDescriptors()
				.Where(d => d.Characteristics.Length >= 1)
				.Where(static d => d is not VideoForWindowsDeviceDescriptor)
				.SelectMany(static d => d.Characteristics, static (d, c) => new MyCaptureDeviceDescriptor(d, c))
				.ToArray();

			foreach (var item in devicesAndCharacteristics)
			{
				Logger.LogTrace($"Cached device: {item.Descriptor.Name} with characteristic: {item.Characteristic}");
			}

			if (devicesAndCharacteristics.Length == 0)
			{
				throw new InvalidOperationException("No camera devices available.");
			}

			return devicesAndCharacteristics.First();
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException("Failed to enumerate capture devices.", ex);
		}
	}), LazyThreadSafetyMode.ExecutionAndPublication);

	// Constructed capture device.
    private static CaptureDevice? CaptureDevice;

    private static Action<Bitmap?> UpdateQrImageFn = (bitmap) => { };

	public ShowQrCameraDialogViewModel(UiContext context, Network network) : base(context)
    {
        _network = network;

        SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: true);
    }

    protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
    {
        base.OnNavigatedTo(isInHistory, disposables);

		UpdateQrImageFn = (bitmap) => { QrImage = bitmap; };

		_ = RunCameraLoopAsync(); // fire-and-forget
    }

    protected override void OnNavigatedFrom(bool isInHistory)
    {
		// Fire and forget.
		Dispatcher.UIThread.InvokeAsync(async () =>
		{
			UpdateQrImageFn = (bitmap) => { };

			if (CaptureDevice is not null)
			{
				Logger.LogDebug("Stopping...");
				await CaptureDevice.StopAsync();
				Logger.LogDebug("Stopped");

				// await CaptureDevice.DisposeAsync();
				// Logger.LogDebug("Disposed");

				// CaptureDevice = null;
			}
		});

        base.OnNavigatedFrom(isInHistory);
    }

    private async Task RunCameraLoopAsync()
    {
        try
        {
			await Dispatcher.UIThread.InvokeAsync(async () =>
			{
				if (CaptureDevice is null)
				{
					var wrapper = DeviceWrapper.Value;

					Console.WriteLine("OpenAsync");
					CaptureDevice = await wrapper.Descriptor.OpenAsync(
						wrapper.Characteristic,
						// ct: cancellationToken,
						pixelBufferArrived: OnPixelBufferArrivedAsync
					);
				}

            	Logger.LogDebug("Starting");
				try 
				{

					
            		await CaptureDevice!.StartAsync();
					Logger.LogDebug("Started");
				} 
				catch (Exception e)
				{
					Logger.LogDebug("Exception while starting capture", e);
				}
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

		try {
			ArraySegment<byte> image = bufferScope.Buffer.ReferImage();

#pragma warning disable CA2000 // Dispose objects before losing scope
			var bitmap = new Bitmap(image.AsStream());
#pragma warning restore CA2000 // Dispose objects before losing scope

			// `bitmap` is copied, so we can release pixel buffer now.
			bufferScope.ReleaseNow();

			// Dispatcher.UIThread.Post(() =>
			// {
			// 	UpdateQrImageFn(bitmap);
			// }
			// );
		} 
		catch (Exception e)
		{
			Logger.LogDebug($"OnPixelBufferArrivedAsync exception: {e.Message}", e);
		}
    }

    private record MyCaptureDeviceDescriptor(CaptureDeviceDescriptor Descriptor, VideoCharacteristics Characteristic);
}
