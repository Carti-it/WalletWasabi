using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NBitcoin;
using System.Reactive.Linq;
using WalletWasabi.Fluent.Models.UI;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;
using WalletWasabi.Userfacing;
using FlashCap;
using FlashCap.Devices;
using FlashCap.Utilities;
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

namespace WalletWasabi.Fluent.ViewModels.Dialogs;

[NavigationMetaData(Title = "Camera", NavigationTarget = NavigationTarget.CompactDialogScreen)]
public partial class ShowQrCameraDialogViewModel : DialogViewModelBase<string?>
{
	private readonly Network _network;
	private readonly QRCodeReader _decoder = new();

	[AutoNotify] private Bitmap? _qrImage;
	[AutoNotify] private string _errorMessage = "";
	[AutoNotify] private string _qrContent = "";

	private CancellationTokenSource? _cts;

	public ShowQrCameraDialogViewModel(UiContext context, Network network)
	{
		_network = network;

		SetupCancel(enableCancel: true, enableCancelOnEscape: true, enableCancelOnPressed: true);
		UiContext = context;
	}

	protected override void OnNavigatedTo(bool isInHistory, CompositeDisposable disposables) // disposables param kept for compatibility, but unused now
	{
		base.OnNavigatedTo(isInHistory, disposables);

		_cts = new CancellationTokenSource();
		_ = RunCameraLoopAsync(_cts.Token); // fire-and-forget background task
	}

	protected override void OnNavigatedFrom(bool isInHistory)
	{
		_cts?.Cancel();
		_cts?.Dispose();
		_cts = null;

		base.OnNavigatedFrom(isInHistory);
	}

	private async Task RunCameraLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			if (!Dispatcher.UIThread.CheckAccess())
			{
				throw new InvalidOperationException("Camera initialization should start from UI thread.");
			}

			Console.WriteLine("Get capture devices");
			var devices = new CaptureDevices();

			Console.WriteLine("Enumerate descriptors");
			var devicesAndCharacteristics = devices
				.EnumerateDescriptors()
				.Where(static d => d is not VideoForWindowsDeviceDescriptor)
				.SelectMany(static d => d.Characteristics, static (d, c) => new { d, c })
				.ToArray();

			foreach (var item in devicesAndCharacteristics)
			{
				Logger.LogTrace($"Found device: {item.d.Name} with characteristic: {item.c}");
			}

			var pair = devicesAndCharacteristics.FirstOrDefault()
				?? throw new InvalidOperationException("Could not find a camera device.");

			Console.WriteLine("OpenAsync");
			await using var device = await pair.d.OpenAsync(
				pair.c,
				ct: cancellationToken,
				pixelBufferArrived: scope =>
				{
					// This callback is called from FlashCap's internal thread (not UI)
					var decoded = Decode(scope);
					var bitmap = new Bitmap(scope.Buffer.ReferImage().AsStream());

					// Post updates to UI thread
					Dispatcher.UIThread.Post(() =>
					{
						if (cancellationToken.IsCancellationRequested)
						{
							return;
						}

						QrImage = bitmap; // always show latest frame

						if (!string.IsNullOrEmpty(decoded))
						{
							var parseResult = AddressParser.Parse(decoded, _network);

							if (parseResult.IsOk)
							{
								// Valid address → close dialog with result
								Close(DialogResultKind.Normal, decoded);
							}
							else
							{
								// Invalid → show error, keep scanning
								ErrorMessage = parseResult.Error ?? "Invalid address";
								QrContent = decoded;
							}
						}
					});
				});

			Console.WriteLine("Started");
			await device.StartAsync(cancellationToken);

			// Keep running until cancelled (via dialog close or valid QR)
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(100, cancellationToken); // light polling / keep-alive
			}

			Console.WriteLine("Stopping...");
			await device.StopAsync(cancellationToken);
			Console.WriteLine("Stopped");
		}
		catch (OperationCanceledException)
		{
			// Normal cancellation → silent
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Camera error: {ex.Message}");

			Dispatcher.UIThread.Post(async () =>
			{
				Close();
				await ShowErrorAsync(Title, ex.Message, "Something went wrong", NavigationTarget.CompactDialogScreen);
			});
		}
	}

	private string Decode(PixelBufferScope scope)
	{
		using var bitmap = SKBitmap.Decode(scope.Buffer.ReferImage());
		var source = new SKBitmapLuminanceSource(bitmap);
		var binary = new BinaryBitmap(new HybridBinarizer(source));
		return _decoder.decode(binary)?.Text ?? "";
	}
}