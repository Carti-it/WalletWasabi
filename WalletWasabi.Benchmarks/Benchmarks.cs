using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Secp256k1;
using System.IO;
using System.Linq;
using System.Text.Json;
using WalletWasabi.Extensions;
using WalletWasabi.Wallets.SilentPayment;

namespace WalletWasabi.Benchmarks;

/// <example>
/// <code>
/// dotnet restore --force-evaluate && dotnet build -f net10.0 -c Release && dotnet run -f net10.0 -c Release -- --runtimes net10.0
/// </code>
/// </example>
[MemoryDiagnoser]
[EventPipeProfiler(EventPipeProfile.CpuSampling)]
public class Benchmarks
{
	private static readonly SilentPaymentTestVector[]? Vectors;

	static Benchmarks()
	{
		var json = File.ReadAllText("D:\\work\\WalletWasabi\\WalletWasabi\\WalletWasabi.Tests\\UnitTests\\Data\\SilentPaymentTestVectors.json");
		Vectors = JsonSerializer.Deserialize<SilentPaymentTestVector[]>(json);
	}

	[Benchmark]
	public void Master()
	{
		foreach (var item in Vectors!)
		{
			SilentPaymentTests.TestVectors(item, false);
		}
	}

	[Benchmark]
	public void Pr()
	{
		foreach (var item in Vectors!)
		{
			SilentPaymentTests.TestVectors(item, true);
		}
	}
}

public static class SilentPaymentTests
{
	public static void TestVectors(SilentPaymentTestVector test, bool v)
	{
		// Receiving functionality

		// message and auxiliary data used in signature
		// see: https://github.com/bitcoinops/taproot-workshop/blob/master/1.1-schnorr-signatures.ipynb
		var msg = NBitcoin.Crypto.Hashes.SHA256(Encoders.ASCII.DecodeData("message"));
		var aux = NBitcoin.Crypto.Hashes.SHA256(Encoders.ASCII.DecodeData("random auxiliary data"));

		foreach (var (given, expected) in test.receiving)
		{
			var (givenInputs, givenOutputs, keyMaterial, labels) = given;
			try
			{
				var prevOuts = givenInputs.Select(x => OutPoint.Parse(x.txid + "-" + x.vout)).ToArray();
				var pubKeys = givenInputs.Select(ExtractPubKey).DropNulls().ToArray();
				if (pubKeys.Length == 0)
				{
					continue; // if there are no pubkeys then nothing can be done
				}

				// Parse key material (scan and spend keys)
				using var scanKey = ParsePrivKey(keyMaterial.scan_priv_key);
				using var spendKey = ParsePrivKey(keyMaterial.spend_priv_key);

				// Addresses
				var baseAddress = new SilentPaymentAddress(0, scanKey.CreatePubKey(), spendKey.CreatePubKey());

				// Creates a lookup table Dic<SilentPaymentAddress, (ECPrivKey labelSecret, ECPubKey labelPubKey)>
				Func<(LabelInfo LabelInfo, SilentPaymentAddress Address), SilentPaymentAddress> keySelector = x => x.Address;
				var addressesTable = labels
					.Select(label => SilentPayment.CreateLabel(scanKey, (uint)label))
					.Select(labelSecret => new LabelInfo.Full(labelSecret, labelSecret.CreatePubKey()))
					.Select(labelInfo => (LabelInfo: (LabelInfo)labelInfo, Address: baseAddress.DeriveAddressForLabel(labelInfo.PubKey)!)) // each label has a different address
					.Prepend((LabelInfo: new LabelInfo.None(), baseAddress))
					.ToDictionary(keySelector, x => x.LabelInfo);

				var addresses = addressesTable.Keys.ToArray();
				var expectedAddresses = expected.addresses.Select(x => SilentPaymentAddress.Parse(x, Network.Main));
				//Assert.Equal(expectedAddresses, addresses);

				var sharedSecret = SilentPayment.ComputeSharedSecretReceiver(prevOuts, pubKeys, scanKey);

				// Outputs
				var givenOutputPubKeys = givenOutputs.Select(ParseXOnlyPubKey).ToArray();

				var detectedOutputPubKeys = v
					? SilentPayment.GetPubKeysNew(addresses, sharedSecret, givenOutputPubKeys)
					: SilentPayment.GetPubKeys(addresses, sharedSecret, givenOutputPubKeys);
			}
			catch (InvalidOperationException e) when (e.Message.Contains("infinite") && test.comment.Contains("point at infinity"))
			{
				// ignore because it is expected to fail;
			}
		}

		ECXOnlyPubKey ParseXOnlyPubKey(string pk) =>
			ECXOnlyPubKey.Create(Encoders.Hex.DecodeData(pk));

		ECPrivKey ParsePrivKey(string pk) =>
			ECPrivKey.Create(Encoders.Hex.DecodeData(pk));
	}


	private static GE? ExtractPubKey(ReceivingVin vin)
	{
		var spk = Script.FromHex(vin.prevout.scriptPubKey.hex);
		var scriptSig = Script.FromHex(vin.scriptSig!);
		var txInWitness = string.IsNullOrEmpty(vin.txinwitness) ? null : new WitScript(Encoders.Hex.DecodeData(vin.txinwitness));
		return SilentPayment.ExtractPubKey(scriptSig, txInWitness!, spk);
	}
}

#pragma warning disable IDE1006 // Naming Styles
public record ScriptPubKey(string hex);
public record Output(ScriptPubKey scriptPubKey);

public record ReceivingExpectedOutput(string priv_key_tweak, string pub_key, string signature);
public record ReceivingVin(string txid, int vout, Output prevout, string? scriptSig, string? txinwitness);
public record SendingVin(string txid, int vout, string private_key, Output prevout);
public record SendingGiven(SendingVin[] vin, string[] recipients);

public record KeyMaterial(string spend_priv_key, string scan_priv_key);
public record ReceivingGiven(ReceivingVin[] vin, string[] outputs, KeyMaterial key_material, int[] labels);
public record SendingExpected(string[][] outputs);

public record ReceivingExpected(string[] addresses, ReceivingExpectedOutput[] outputs);

public record Sending(SendingGiven given, SendingExpected expected);
public record Receiving(ReceivingGiven given, ReceivingExpected expected);

public record SilentPaymentTestVector(string comment, Sending[] sending, Receiving[] receiving)
{
	public override string ToString() => comment;
}
#pragma warning restore IDE1006 // Naming Styles

public abstract record LabelInfo
{
	public record Full(ECPrivKey Secret, ECPubKey PubKey) : LabelInfo;

	public record None : LabelInfo;
}


public static class Program
{
	public static void Main()
	{
		_ = BenchmarkRunner.Run<Benchmarks>();
	}
}
