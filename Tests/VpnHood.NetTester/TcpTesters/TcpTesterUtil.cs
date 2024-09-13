namespace VpnHood.NetTester.TcpTesters;

public static class TcpTesterUtil
{
    public static async Task ReadData(Stream stream, long byteCount, Speedometer speedometer,
        CancellationToken cancellationToken)
    {
        // loop and read 10k random data from stream in each iteration.
        var buffer = new byte[1024 * 10];
        for (var i = 0; i < byteCount; i += buffer.Length) {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (read == 0)
                break;
            
            speedometer.AddRead(read);
        }
    }

    public static async Task WriteRandomData(Stream stream, long byteCount, Speedometer speedometer,
        CancellationToken cancellationToken)
    {
        // loop and write 10k random data to stream in each iteration.
        var buffer = new byte[1024 * 10];
        new Random().NextBytes(buffer);
        for (var i = 0; i < byteCount; i += buffer.Length) {
            await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
            speedometer.AddWrite(buffer.Length);
        }
    }
}