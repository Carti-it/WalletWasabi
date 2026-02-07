using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FlashCap;
using FlashCap.Devices;
using FlashCap.Utilities;
using SkiaSharp;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WalletWasabi.Logging;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.SkiaSharp;

namespace WalletWasabi.Fluent.Models.UI;

public interface IQrCodeReader
{
	bool IsPlatformSupported { get; }

	IObservable<(string decoded, Bitmap bitmap)> Read();
}

public partial class QrCodeReader : IQrCodeReader
{
	private readonly QRCodeReader _decoder = new();

	public bool IsPlatformSupported =>
		RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
		RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
		RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

	public IObservable<(string decoded, Bitmap bitmap)> Read()
	{
		return Observable.Create(
			async (IObserver<(string, Bitmap)> result, CancellationToken cancellationToken) =>
			{
				if (!Dispatcher.UIThread.CheckAccess())
				{
					throw new NotSupportedException("This method must be called on the UI thread.");
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

				var pair0 = devicesAndCharacteristics.FirstOrDefault()
					?? throw new InvalidOperationException("Could not find a device.");

				int i = 0;

				Console.WriteLine("OpenAsync");
				using var device = await pair0.d.OpenAsync(
					pair0.c,
					ct: cancellationToken,
					pixelBufferArrived: scope =>
					{
						i++;
						if (i % 10 == 0)
						{
							Console.WriteLine("Decode");
						}

						var decoded = Decode(scope);
						var bitmap = new Bitmap(scope.Buffer.ReferImage().AsStream());

						result.OnNext((decoded, bitmap));
					});

				var tcs = new TaskCompletionSource<object?>();
				cancellationToken.Register(() =>
				{
					Console.WriteLine("Cancellation token SET");
					tcs.TrySetResult(default);
				});

				if (!Dispatcher.UIThread.CheckAccess())
				{
					throw new NotSupportedException("#1");
				}

				// Start capturing.
				await device.StartAsync(cancellationToken);
				Console.WriteLine("Started");

				if (!Dispatcher.UIThread.CheckAccess())
				{
					throw new NotSupportedException("#2");
				}

				// Wait until cancellation is requested.
				await tcs.Task;
				Console.WriteLine("Canceled");

				if (!Dispatcher.UIThread.CheckAccess())
				{
					throw new NotSupportedException("#3");
				}

				// Stop capturing.
				await device.StopAsync(cancellationToken);
				Console.WriteLine("Stopped");

				if (!Dispatcher.UIThread.CheckAccess())
				{
					throw new NotSupportedException("#4");
				}
			});
	}

	private string Decode(PixelBufferScope scope)
	{
		using var bitmap = SKBitmap.Decode(scope.Buffer.ReferImage());
		var source = new SKBitmapLuminanceSource(bitmap);
		var binary = new BinaryBitmap(new HybridBinarizer(source));
		return _decoder.decode(binary)?.Text ?? "";
	}
}
