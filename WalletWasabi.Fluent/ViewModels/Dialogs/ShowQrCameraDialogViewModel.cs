using Avalonia.Threading;
using NBitcoin;
using System.Reactive.Linq;
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
using System.Windows.Input;
using System.Collections.ObjectModel;

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

	private static readonly Lazy<CaptureDevices> CaptureDevices = new(() => new CaptureDevices());

	public ObservableCollection<CaptureDeviceDescriptor?> DeviceList { get; } = new();

	public CaptureDeviceDescriptor? Device { get; set; }
	public ObservableCollection<VideoCharacteristics> CharacteristicsList { get; } = new();
	public VideoCharacteristics? Characteristics { get; set; }

    private static Action<SKBitmap?> UpdateQrImageFn = (bitmap) => { };

	public ICommand OpenCommand { get; }

	public ICommand StartCaptureCommand { get; }

	private CaptureDevice? _captureDevice;

	public ShowQrCameraDialogViewModel(UiContext context, Network network) : base(context)
    {
        _network = network;
		OpenCommand = ReactiveCommand.Create(() =>
		{
            Console.WriteLine("Opened()");

            // Enumerate capture devices:
            var devices = new CaptureDevices();

            // Store device list into the combo box.
            DeviceList.Clear();

            Console.WriteLine("Opened(): Enumerate descriptors");
            foreach (var descriptor in devices.EnumerateDescriptors().
                // You could filter by device type and characteristics.
                //Where(d => d.DeviceType == DeviceTypes.DirectShow).  // Only DirectShow device.
                Where(d => d.Characteristics.Length >= 1))             // One or more valid video characteristics.
            {
				Console.WriteLine("Opened(): Adding device");
                DeviceList.Add(descriptor);
            }

			Console.WriteLine("Opened(): Assign device");
            Device = DeviceList.FirstOrDefault();

            // Or, you could choice from device descriptor:
            CharacteristicsList.Clear();

			if (Device is {} device) 
			{
				foreach (var characteristics in device.Characteristics)
				{
					if (characteristics.PixelFormat != PixelFormats.Unknown)
					{
						CharacteristicsList.Add(characteristics);
					}
				}

				Characteristics = CharacteristicsList.FirstOrDefault();

				Console.WriteLine("Opened(): Assign char");
				Characteristics = Device?.Characteristics?.FirstOrDefault();
			}

            Console.WriteLine("Opened(-)");
		});
		StartCaptureCommand = ReactiveCommand.CreateFromTask(RunCameraLoopAsync);

        SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: true);
    }

    protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables)
    {
        base.OnNavigatedTo(isInHistory, disposables);

        _cts = new CancellationTokenSource();
		UpdateQrImageFn = (bitmap) => { QrImage = bitmap; };
    }

    protected override void OnNavigatedFrom(bool isInHistory)
    {
		UpdateQrImageFn = (bitmap) => { };

		// Fire and forget.
		_ = Dispatcher.UIThread.InvokeAsync(async () => 
		{
			if (_captureDevice is not null) 
			{
				Console.WriteLine("Stopping...");
				await _captureDevice.StopAsync();
				Console.WriteLine("Stopped");
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
			if (Device is {} device && Characteristics is {} characteristics) 
			{
				Console.WriteLine($"RunCameraLoopAsync: Opening: {device.Name}");
				Console.WriteLine($"RunCameraLoopAsync: -- {characteristics}");
				_captureDevice = await device.OpenAsync(characteristics, OnPixelBufferArrivedAsync, CancellationToken.None);
			}

			Console.WriteLine("Starting");
			await _captureDevice!.StartAsync(cancellationToken);
			Console.WriteLine("Started");
        }
        catch (OperationCanceledException)
        {
            // expected
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Camera error: {ex.Message}");
			Close();
			await ShowErrorAsync(Title, ex.Message, "Something went wrong", NavigationTarget.CompactDialogScreen);
        } 
    }


    private async Task OnPixelBufferArrivedAsync(PixelBufferScope bufferScope)
    {
		Console.WriteLine("OnPixelBufferArrivedAsync...");

        ////////////////////////////////////////////////
        // Pixel buffer has arrived.
        // NOTE: Perhaps this thread context is NOT UI thread.
        ArraySegment<byte> image = bufferScope.Buffer.ReferImage();

		// Decode image data to a bitmap:
		SKBitmap bitmap = SKBitmap.Decode(image);

        // Capture statistics variables.
        var frameIndex = bufferScope.Buffer.FrameIndex;
        var timestamp = bufferScope.Buffer.Timestamp;

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

		// `bitmap` is copied, so we can release pixel buffer now.
        bufferScope.ReleaseNow();
    }

    // private string Decode(PixelBufferScope scope)
    // {
    //     using var bitmap = SKBitmap.Decode(scope.Buffer.ReferImage());
    //     var source = new SKBitmapLuminanceSource(bitmap);
    //     var binary = new BinaryBitmap(new HybridBinarizer(source));
    //     return _decoder.decode(binary)?.Text ?? "";
    // }

    // Helper record to hold what we need (adjust based on actual types in FlashCap)
    private record MyCaptureDeviceDescriptor(CaptureDeviceDescriptor Descriptor, VideoCharacteristics Characteristic);
}
