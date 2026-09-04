namespace WalletWasabi.Helpers;

public static class ByteHelpers
{
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
