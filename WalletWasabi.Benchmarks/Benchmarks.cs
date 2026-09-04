using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using NBitcoin.DataEncoders;
using WalletWasabi.Helpers;

namespace WalletWasabi.Benchmarks;

/// <example>
/// <code>
/// dotnet restore --force-evaluate && dotnet build -f net10.0 -c Release && dotnet run -f net10.0 -c Release -- --runtimes net10.0
/// </code>
/// </example>
[MemoryDiagnoser]
public class Benchmarks
{
	private static readonly byte[] NUMS =
		Encoders.Hex.DecodeData("50929b74c1a04954b78b4b6035e97a5e078a5a0f28ec96d547bfee9ace803ac0");

	private static readonly byte[] TEST =
		Encoders.Hex.DecodeData("50929b74c1a04954b78b4b6035e97a5e078a5a0f28ec96d547bfee9ace803ac1");


	[Benchmark]
	public bool Master()
	{
		return ByteHelpers.CompareFastUnsafe(NUMS, TEST);
	}

	[Benchmark]
	public bool Pr()
	{
		return NUMS.AsSpan().SequenceEqual(TEST.AsSpan());
	}
}

public static class Program
{
	public static void Main()
	{
		_ = BenchmarkRunner.Run<Benchmarks>();
	}
}
