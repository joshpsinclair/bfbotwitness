using System;
using System.Runtime.Serialization.Formatters.Binary;
using System.IO;
using System.Runtime.Serialization;
public static class CloningService
{
	public static T Clone<T>(this T source)
	{
		// Don't serialize a null object, simply return the default for that object
		if (Object.ReferenceEquals(source, null))
		{
			return default(T);
		}
		
		IFormatter formatter = new BinaryFormatter();
		using (MemoryStream stream = new MemoryStream())
		{
			formatter.Serialize(stream, source);
			stream.Seek(0, SeekOrigin.Begin);
			return (T)formatter.Deserialize(stream);
		}
	}
}