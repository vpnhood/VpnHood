# TCP Stack Integration Test

This test validates the complete integration between the LocalTcpStack and WinDivert adapter by performing a comprehensive 10MB echo test.

## Test Flow

### 1. Setup Phase
- Creates a `LocalTcpStack` instance
- Configures `WinDivertVpnAdapter` to capture traffic to `11.0.0.0/8` network
- Sets up TCP stack integration via callback mechanism
- Starts an echo server listening on `11.0.0.1:8080`

### 2. Packet Capture Integration
- WinDivert adapter captures outbound packets to `11.0.0.x` addresses
- Packets are fed to the TCP stack via `tcpStack.TryProcessPacket()`
- TCP stack processes SYN, ACK, and data packets
- Response packets are sent back through the adapter

### 3. Echo Test Execution
- Generates 10MB of random test data
- Connects to `11.0.0.1:8080` using standard `TcpClient`
- Sends data in 8KB chunks
- Receives echoed data and validates byte-for-byte accuracy

### 4. Verification
- Ensures all 10MB is sent and received correctly
- Compares received data with original test data
- Validates TCP stack can handle large data transfers

## Key Components Tested

### LocalTcpStack
- Packet parsing and TCP state management
- SYN-ACK handshake processing
- Data segmentation and reassembly
- Connection lifecycle management

### Integration Layer
- Packet capture from WinDivert
- Bidirectional packet flow between adapter and TCP stack
- Proper packet serialization/deserialization

### LocalTcpStream
- Stream interface compatibility
- Async read/write operations
- Large data transfer handling
- Connection cleanup

## Technical Details

### Reflection Usage
The test uses reflection to access protected methods of `WinDivertVpnAdapter` since it's designed for internal framework use. In production, these would be accessible through proper public APIs.

### Test Data
- 10MB random data ensures comprehensive testing
- Chunked transfer (8KB) simulates real-world scenarios
- Byte-by-byte comparison ensures data integrity

### Timeout Handling
- 5-minute timeout prevents indefinite hanging
- Proper cleanup in finally blocks
- Cancellation token support throughout

## Expected Results

When successful, this test proves:
1. ? TCP stack correctly implements TCP protocol basics
2. ? Integration with WinDivert packet capture works
3. ? Large data transfers maintain integrity
4. ? Stream interface provides compatible API
5. ? Memory management handles 10MB+ transfers efficiently

## Running the Test

```bash
dotnet test VpnHood.Core.TcpStack.Test --filter "TestTcpStackWithWinDivertAdapter_10MbEcho_ShouldSucceed"
```

**Note**: This test requires administrator privileges due to WinDivert's need for packet capture capabilities.