using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VpnHood.Tunneling
{
    public static class StreamUtil
    {
        public static byte[] ReadWaitForFill(Stream stream, int count)
        {
            var buffer = new byte[count];
            if (!ReadWaitForFill(stream, buffer, 0, buffer.Length))
                return null;
            return buffer;
        }

        public static async Task<byte[]> ReadWaitForFillAsync(Stream stream, int count)
        {
            var buffer = new byte[count];
            if (!await ReadWaitForFillAsync(stream, buffer, 0, buffer.Length))
                return null;
            return buffer;
        }

        public static bool ReadWaitForFill(Stream stream, byte[] buffer, int startIndex, int count)
        {
            var totalReaded = 0;
            while (totalReaded != count)
            {
                var read = stream.Read(buffer, startIndex + totalReaded, count - totalReaded);
                totalReaded += read;
                if (read == 0)
                    return false;
            }

            return true;
        }

        public static async Task<bool> ReadWaitForFillAsync(Stream stream, byte[] buffer, int startIndex, int count)
        {
            var totalReaded = 0;
            while (totalReaded != count)
            {
                var read = await stream.ReadAsync(buffer, startIndex + totalReaded, count - totalReaded);
                totalReaded += read;
                if (read == 0)
                    return false;
            }

            return true;
        }

        public static T ReadJson<T>(Stream stream, int maxLength = 0xFFFF)
        {
            // read length
            var buffer = ReadWaitForFill(stream, 4);
            if (buffer == null)
                throw new Exception($"Could not read {typeof(T).Name}");

            // check json size
            var jsonSize = BitConverter.ToInt32(buffer);
            if (jsonSize == 0)
                throw new Exception("json length is zero!");
            if (jsonSize > maxLength)
                throw new Exception($"json length is too big! It should be less than {maxLength} bytes but it was {jsonSize} bytes");

            // read json body...
            buffer = ReadWaitForFill(stream, jsonSize);
            if (buffer == null)
                throw new Exception("Could not read Message Length!");

            // serialize the request
            var json = Encoding.UTF8.GetString(buffer);
            return JsonSerializer.Deserialize<T>(json);
        }

        public static async Task<T> ReadJsonAsync<T>(Stream stream, int maxLength = 0xFFFF)
        {
            // read length
            var buffer = await ReadWaitForFillAsync(stream, 4);
            if (buffer == null)
                throw new Exception($"Could not read {typeof(T).Name}");

            // check json size
            var jsonSize = BitConverter.ToInt32(buffer);
            if (jsonSize == 0)
                throw new Exception("json length is zero!");
            if (jsonSize > maxLength)
                throw new Exception($"json length is too big! It should be less than {maxLength} bytes but it was {jsonSize} bytes");

            // read json body...
            buffer = await ReadWaitForFillAsync(stream, jsonSize);
            if (buffer == null)
                throw new Exception("Could not read Message Length!");

            // serialize the request
            var json = Encoding.UTF8.GetString(buffer);
            return JsonSerializer.Deserialize<T>(json);
        }

        private static byte[] ObjectToJsonBuffer(object obj)
        {
            var jsonBuffer = JsonSerializer.SerializeToUtf8Bytes(obj);
            var buffer = new byte[4 + jsonBuffer.Length];
            Buffer.BlockCopy(BitConverter.GetBytes(jsonBuffer.Length), 0, buffer, 0, 4);
            Buffer.BlockCopy(jsonBuffer, 0, buffer, 4, jsonBuffer.Length);
            return buffer;
        }

        public static void WriteJson(Stream stream, object obj)
            => stream.Write(ObjectToJsonBuffer(obj));

        public static Task WriteJsonAsync(Stream stream, object obj)
            => stream.WriteAsync(ObjectToJsonBuffer(obj)).AsTask();

    }
}
