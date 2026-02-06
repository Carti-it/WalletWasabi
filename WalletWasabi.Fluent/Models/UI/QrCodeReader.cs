using Avalonia.Media.Imaging;
using FlashCap;
using FlashCap.Utilities;
using FlashCap.Devices;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using ZXing.QrCode;
using SkiaSharp;
using System.Threading;
using System.Threading.Tasks;
using ZXing.SkiaSharp;
using ZXing.Common;
using ZXing;
using WalletWasabi.Logging;

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
				var devices = new CaptureDevices();
				var devicesAndCharacteristics = devices
					.EnumerateDescriptors()
					.Where(static d => d is not VideoForWindowsDeviceDescriptor)
					.SelectMany(static d => d.Characteristics, static (d, c) => new { d, c })
					.ToArray();

				foreach (var item in devicesAndCharacteristics)
				{
					Logger.LogTrace($"Found device: {item.d.Name} with characteristic: {item.c}");
				}

				var device = devicesAndCharacteristics.FirstOrDefault()
					?? throw new InvalidOperationException("Could not find a device.");

				await using var capture = await device.d
					.OpenAsync(
						device.c,
						ct: cancellationToken,
						pixelBufferArrived: scope =>
						{
							var decoded = Decode(scope);
							var bitmap = new Bitmap(scope.Buffer.ReferImage().AsStream());

							result.OnNext((decoded, bitmap));
						})
					.ConfigureAwait(false);

				var tcs = new TaskCompletionSource<object?>();
				cancellationToken.Register(() => tcs.TrySetResult(default));

				// Start capturing.
				await capture.StartAsync(cancellationToken).ConfigureAwait(false);

				// Wait until cancellation is requested.
				await tcs.Task.ConfigureAwait(false);

				// Stop capturing.
				await capture.StopAsync(cancellationToken).ConfigureAwait(false);
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
