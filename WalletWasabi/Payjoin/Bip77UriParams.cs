using System.Diagnostics.CodeAnalysis;

namespace WalletWasabi.Payjoin;

/// <summary>
/// Textual helpers for BIP 77 <c>pj=</c> endpoint URLs.
/// </summary>
/// <remarks>
/// An example of a BIP 77 endpoint URL is here:
/// <c>bitcoin:tb1q6q6de88mj8qkg0q5lupmpfexwnqjsr4d2gvx2p?amount=0.00666666&pjos=0&pj=HTTPS://PAYJO.IN/TXJCGKTKXLUUZ%23EX1WKV8CEC-OH1QYPM59NK2LXXS4890SUAXXYT25Z2VAPHP0X7YEYCJXGWAG6UG9ZU6NQ-RK1Q0DJS3VVDXWQQTLQ8022QGXSX7ML9PHZ6EDSF6AKEWQG758JPS2EV</c>
/// and decoded <c>pj</c> parameter is:
/// <c>HTTPS://PAYJO.IN/TXJCGKTKXLUUZ#EX1WKV8CEC-OH1QYPM59NK2LXXS4890SUAXXYT25Z2VAPHP0X7YEYCJXGWAG6UG9ZU6NQ-RK1Q0DJS3VVDXWQQTLQ8022QGXSX7ML9PHZ6EDSF6AKEWQG758JPS2EV</c>
/// where you can notice the <c>#</c> followed by comma separated fragment parameters (like <c>RK1</c> and <c>OH1</c>).
/// </remarks>
/// <seealso href="https://github.com/bitcoin/bips/blob/master/bip-0077.md"/>
public static class Bip77UriParams
{
	/// <summary>A pj endpoint is BIP 77 when its fragment carries the receiver-key and OHTTP-keys params.</summary>
	public static bool IsBip77(string pjEndpoint) =>
		TryGetFragmentParam(pjEndpoint, "RK1", out _) && TryGetFragmentParam(pjEndpoint, "OH1", out _);

	/// <summary>The receiver's ephemeral public key (opaque bech32 string) — the session-dedup key.</summary>
	public static bool TryGetReceiverKey(string pjEndpoint, [NotNullWhen(true)] out string? receiverKey) =>
		TryGetFragmentParam(pjEndpoint, "RK1", out receiverKey);

	private static bool TryGetFragmentParam(string pjEndpoint, string prefix, [NotNullWhen(true)] out string? value)
	{
		value = null;

		int fragmentStart = pjEndpoint.IndexOf('#', StringComparison.Ordinal);
		if (fragmentStart < 0 || fragmentStart == pjEndpoint.Length - 1)
		{
			return false;
		}

		// '-' is outside the bech32 character set, so splitting on both delimiters is safe.
		foreach (string param in pjEndpoint[(fragmentStart + 1)..].Split('+', '-'))
		{
			if (param.StartsWith(prefix, StringComparison.Ordinal))
			{
				value = param;
				return true;
			}
		}

		return false;
	}
}
