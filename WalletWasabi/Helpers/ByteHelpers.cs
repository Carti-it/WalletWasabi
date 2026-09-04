namespace WalletWasabi.Helpers;

public static class ByteHelpers
{
	// https://stackoverflow.com/questions/415291/best-way-to-combine-two-or-more-byte-arrays-in-c-sharp
	/// <summary>
	/// Fastest byte array concatenation in C#
	/// </summary>
	public static byte[] Combine(params byte[][] arrays)
	{
		byte[] ret = new byte[arrays.Sum(x => x.Length)];
		int offset = 0;
		foreach (byte[] data in arrays)
		{
			Buffer.BlockCopy(data, 0, ret, offset, data.Length);
			offset += data.Length;
		}
		return ret;
	}

	public static bool CompareFast(byte[]? array1, byte[]? array2)
	{
		if (array1 == array2)
		{
			return true;
		}

		if (array1 is null || array2 is null)
		{
			return false;
		}

		return array1.AsSpan().SequenceEqual(array2.AsSpan());
	}
}
