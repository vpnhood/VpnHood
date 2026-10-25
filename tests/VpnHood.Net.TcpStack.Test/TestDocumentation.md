# The TcpStack integration tests

`IntegrationTest.cs` runs the stack against the local kernel through the WinDivert adapter: a real TCP client in
the test process connects to an address the adapter intercepts, its packets reach `LocalTcpStack.ProcessIncoming`,
and the stack's packets go back through the adapter.

- `DiagnosticTest_SmallData_WinDivert`: a 5-byte echo with every packet logged, to see the handshake and the
  data flow when something is wrong.
- `TestTcpStackWithWinDivertAdapter_Echo_ShouldSucceed`: a 20 MB echo through one connection; the server side
  reads from the accepted `LocalTcpStream` and writes the bytes back.

WinDivert is a Windows kernel driver: it needs an elevated process and intercepts the machine's own traffic.
Exclude these tests from a unit run with `--filter FullyQualifiedName!~IntegrationTest`, and run them on a Windows
VM: install the .NET 11 SDK there, copy the test build (`bin/Debug/net11.0`) over, and run
`dotnet test VpnHood.Net.TcpStack.Test.dll --filter FullyQualifiedName~IntegrationTest` as Administrator.
