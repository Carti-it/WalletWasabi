using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NBitcoin;
using NBitcoin.WalletPolicies;
using WalletWasabi.Blockchain.Keys;
using Xunit;
using static WalletWasabi.Blockchain.Keys.WpkhWalletPolicyHelper;

namespace WalletWasabi.Tests.UnitTests.Blockchain.Keys;

public class WpkhWalletPolicyHelperTests
{
	[Fact]
	public void BasicTest()
	{
		Network testNet = Network.TestNet;
		BitcoinEncryptedSecretNoEC encryptedSecret = new(wif: "6PYJxoa2SLZdYADFyMp3wo41RKaKGNedC3vviix4VdjFfrt1LkKDmXmYTM", Network.Main);
		byte[]? chainCode = Convert.FromHexString("D9DAD5377AB84A44815403FF57B0ABC6825701560DAA0F0FCDDB5A52EBE12A6E");
		ExtKey accountPrivateKey = new(encryptedSecret.GetKey(password: "123456"), chainCode);
		KeyPath keyPath = new("84'/0'/0'");
		HDFingerprint masterFingerprint = new(0x2fc4a4f3);

		WalletPolicies walletPolicies = WpkhWalletPolicyHelper.Get(testNet, masterFingerprint, accountPrivateKey, keyPath);

		Console.WriteLine("Using xprv walletPolicy:");
		for (int i = 0; i < 5; i++)
		{
			BitcoinAddress addr = DeriveAddress(walletPolicies.PrivateWalletPolicy, AddressIntent.Deposit, i, testNet);
			Console.WriteLine($"xprv Address {i}: {addr}");
			Debug.WriteLine($"xprv Address {i}: {addr}");
		}

		Console.WriteLine("Using xpub walletPolicyXpub:");
		for (int i = 0; i < 5; i++)
		{
			BitcoinAddress addr = DeriveAddress(walletPolicies.PublicWalletPolicy, AddressIntent.Deposit, i, testNet);
			Console.WriteLine($"xpub Address {i}: {addr}");
			Debug.WriteLine($"xpub Address {i}: {addr}");
		}	
	}

	public BitcoinAddress DeriveAddress(WalletPolicy walletPolicy, AddressIntent addressIntent, int index, Network network)
	{
		Miniscript.Scripts expectedScripts = walletPolicy.FullDescriptor.Derive(addressIntent, index).Miniscript.ToScripts();
		BitcoinAddress? result = expectedScripts.ScriptPubKey.GetDestinationAddress(network);
		Assert.NotNull(result);

		return result;
	}
}
