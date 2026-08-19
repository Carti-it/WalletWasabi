using NBitcoin.RPC;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using WalletWasabi.Bases;
using WalletWasabi.BitcoinRpc;
using WalletWasabi.WebClients;

namespace WalletWasabi.WabiSabi.Coordinator.DoSPrevention.Ofac;

/// <summary>
/// Retrieves a list of sanctioned Bitcoin addresses from the OFAC sanctions list.
/// </summary>
/// <remarks>
/// The queried list is the Specially Designated Nationals (SDN) list with the feature type "Digital Currency Address - XBT".
/// The SDN list includes individuals, groups, and entities under programs that are not country-specific.
/// </remarks>
/// <seealso href="https://sanctionslist.ofac.treas.gov/Home/SdnList"/>
public class OfacSdnChecker : PeriodicRunner
{
	private static UserAgentPicker PickRandomUserAgent = UserAgent.GenerateUserAgentPicker(false);

	/// <summary>The URL of the SDN zip file.</summary>
	private const string SdnZipUrl = "https://sanctionslistservice.ofac.treas.gov/api/PublicationPreview/exports/SDN_ADVANCED.ZIP";
	private readonly Uri _listUri;
	private readonly Network _network;
	private readonly OfacSdnParser _ofacParser;
	private readonly Prison _prison;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IRPCClient _rpcClient;

	public OfacSdnChecker(Network network, OfacSdnParser ofacParser, Prison prison, IHttpClientFactory httpClientFactory, IRPCClient rpcClient, Uri? listUri = null)
		: base(TimeSpan.FromDays(1))
	{
		_network = network;
		_ofacParser = ofacParser;
		_prison = prison;
		_httpClientFactory = httpClientFactory;
		_rpcClient = rpcClient;
		_listUri = listUri ?? new Uri(SdnZipUrl);

		Logger.LogInfo($"HTTP client factory type is '{_httpClientFactory.GetType().FullName}'");
	}

	protected override async Task ActionAsync(CancellationToken cancellationToken)
	{
		for (int i = 0; i < 3; i++)
		{
			try
			{
				Logger.LogInfo("Retrieving OFAC sanctions list…");
				Stopwatch sw = Stopwatch.StartNew();

				List<string> list;

				if (_network == Network.Main)
				{
					// Create a new HTTP client with a random User-Agent header to avoid being blocked by the server (the server requires a User-Agent header).
					using var httpClient = _httpClientFactory.CreateClient("ofac");
					httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", PickRandomUserAgent());
					httpClient.DefaultRequestHeaders.Accept.ParseAdd("*/*");

					// Download the zip file and extract the XML stream.
					using var zipStream = await DownloadZipAsync(httpClient, _listUri, cancellationToken).ConfigureAwait(false);
					Logger.LogInfo($"OFAC sanctions list downloaded in {sw.ElapsedMilliseconds} ms.");

					sw.Restart();
					using var xmlStream = Unzip(zipStream);

					list = await _ofacParser.GetSanctionedBtcAddressesAsync(xmlStream, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					// Regtest test.
					list = ["bcrt1qytypjcqy7gdv5lmz95q04r0tus3ehpty7nmptd"];
				}

				var parameters = new ScanTxoutSetParameters()
				{
					Descriptors = list.Select(addr => new ScanTxoutDescriptor($"addr({addr})")).ToArray()
				};

				var response = await _rpcClient.StartScanTxoutSetAsync(parameters, cancellationToken).ConfigureAwait(false);

				if (response.Success)
				{
					var outPointsToBan = new List<OutPoint>();

					foreach (var output in response.Outputs)
					{
						var address = output.Coin.TxOut.ScriptPubKey.GetDestinationAddress(_network);
						var outpoint = output.Coin.Outpoint;
						var txid = outpoint.Hash;
						var amount = output.Coin.Amount;

						Logger.LogInfo($"Found UTXO for sanctioned address {address}: {amount} BTC in transaction {txid} at vout {outpoint.N}.");
					}

					Logger.LogInfo($"Put {outPointsToBan.Count} outpoints to prison.");
					foreach (var outPoint in outPointsToBan)
					{
						_prison.CheatingDetected(outPoint, uint256.Zero);
					}
				}

				Logger.LogInfo($"OFAC sanctions list parsed in {sw.ElapsedMilliseconds} ms. Found {list.Count} sanctioned Bitcoin addresses.");
				break;
			}
			catch (Exception ex)
			{
				Logger.LogError($"Attempt #{i} to retrieve OFAC sanctions list failed.");
				Logger.LogError(ex);
			}
		}
	}

	internal async Task<Stream> DownloadZipAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
	{
		using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
			.ConfigureAwait(false);
		response.EnsureSuccessStatusCode();

		// Buffer to a seekable MemoryStream since ZipArchive needs to seek.
		var buffer = new MemoryStream();
		await response.Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
		buffer.Position = 0;
		return buffer;
	}

	internal static Stream Unzip(Stream zipStream)
	{
		using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

		var zipEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) ??
			throw new InvalidOperationException("No .xml entry found inside SDN_ADVANCED.ZIP.");

		var extracted = new MemoryStream();
		using (var entryStream = zipEntry.Open())
		{
			entryStream.CopyTo(extracted);
		}
		extracted.Position = 0;
		return extracted;
	}
}
