using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.Secp256k1;
using WalletWasabi.Wallets.SilentPayment;

namespace WalletWasabi.Benchmarks;

/// <example>
/// <code>
/// dotnet restore --force-evaluate && dotnet build -f net10.0 -c Release && dotnet run -f net10.0 -c Release -- --runtimes net10.0
/// </code>
/// </example>
[MemoryDiagnoser]
public class Benchmarks
{
	private static readonly OutPoint OutPoint1 = new(Hashes.DoubleSHA256([1]), 0);
	private static readonly OutPoint OutPoint2 = new(Hashes.DoubleSHA256([2]), 0);
	private static readonly OutPoint[] OutPoints = [OutPoint1, OutPoint2];
	private static readonly GE[] GEs = [
		GE.CONST(
			0x6d986544, 0x57ff52b8, 0xcf1b8126, 0x5b802a5b,
			0xa97f9263, 0xb1e88044, 0x93351325, 0x91bc450a,
			0x535c59f7, 0x325e5d2b, 0xc391fbe8, 0x3c12787c,
			0x337e4a98, 0xe82a9011, 0x0123ba37, 0xdd769c7d
			),
		GE.CONST(
			0x23773684, 0x4d209dc7, 0x098a786f, 0x20d06fcd,
			0x070a38bf, 0xc11ac651, 0x03004319, 0x1e2a8786,
			0xed8c3b8e, 0xc06dd57b, 0xd06ea66e, 0x45492b0f,
			0xb84e4e1b, 0xfb77e21f, 0x96baae2a, 0x63dec956
		)];

	private static readonly byte[] PrivateKeyBytes = new byte[32];

	static Benchmarks()
	{
		Random.Shared.NextBytes(PrivateKeyBytes);
	}

	[Benchmark]
	public ECPubKey Master()
	{
		using ECPrivKey privKey = ECPrivKey.Create(PrivateKeyBytes.AsSpan());
		return SilentPayment.ComputeSharedSecretReceiver(OutPoints, GEs, privKey);
	}

	[Benchmark]
	public ECPubKey Pr()
	{
		using ECPrivKey privKey = ECPrivKey.Create(PrivateKeyBytes.AsSpan());
		return SilentPayment.ComputeSharedSecretReceiver(OutPoints, GEs, privKey);
	}
}

public static class Program
{
	public static void Main()
	{
		_ = BenchmarkRunner.Run<Benchmarks>();
	}
}
