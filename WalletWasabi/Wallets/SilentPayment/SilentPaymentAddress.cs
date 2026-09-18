using NBitcoin.DataEncoders;
using NBitcoin.Secp256k1;

namespace WalletWasabi.Wallets.SilentPayment;

public record SilentPaymentAddress(int Version, ECPubKey ScanKey, ECPubKey SpendKey)
{
	public SilentPaymentAddress(int version, PubKey scanKey, PubKey spendKey)
		: this(version, ECPubKey.Create(scanKey.ToBytes()), ECPubKey.Create(spendKey.ToBytes()))
	{}

	public static SilentPaymentAddress Parse(string encoded, Network network)
	{
		var spEncoder = network.GetSilentPaymentBech32Encoder();
		var result = spEncoder.DecodeDataRaw(encoded, out _);
		var version = result[0];
		if (version != 0)
		{
			throw new FormatException("Unexpected version of silent payment code");
		}

		if (result.Length != 107)
		{
			throw new FormatException("Wrong length");
		}

		var data = spEncoder.FromBase32(result.AsSpan(1));
		return new SilentPaymentAddress(
			Version: 0,
			ScanKey: ECPubKey.Create(data.AsSpan(..33)),
			SpendKey: ECPubKey.Create(data.AsSpan(33..)));
	}

	/// <summary>
	/// Export the silent address to the wallet import format.
	/// </summary>
	public string ToWif(Network network)
	{
		Span<byte> keysData = stackalloc byte[66];
		ScanKey.ToBytes().CopyTo(keysData);
		SpendKey.ToBytes().CopyTo(keysData[33..]);

		var spEncoder = network.GetSilentPaymentBech32Encoder();
		var base32 = spEncoder.ToBase32(keysData);

		Span<byte> buffer = stackalloc byte[base32.Length + 1];
		buffer[0] = (byte)Version;
		base32.CopyTo(buffer[1..]);

		return spEncoder.EncodeRaw(buffer, Bech32EncodingType.BECH32M);
	}

	public SilentPaymentAddress DeriveAddressForLabel(ECPubKey mG)
	{
		var bm = (SpendKey.Q.ToGroupElementJacobian() + mG.Q).ToGroupElement();
		return this with {SpendKey = new ECPubKey(bm, null)};
	}
}
