## ProxyServerManager Update - Multiple Proxy Support

### Changes Made

1. **Updated ProxyServerType enum** (`Src/Core/VpnHood.Core.Client.Abstractions/ProxyServerType.cs`)
   - Added support for `Socks4`, `Http`, and `Https` proxy types
   - Maintained backward compatibility with existing `Socks5` type

2. **Refactored ProxyServerManager** (`Src/Core/VpnHood.Core.Client/ClientProxies/ProxyServerManager.cs`)
   - Replaced hardcoded SOCKS5 implementation with a proxy type factory pattern
   - Added `ConnectToProxyAsync` method to handle different proxy types
   - Currently supports SOCKS5 with placeholders for other types
   - Improved logging to include proxy type information
   - Maintained backward compatibility with existing SOCKS5 functionality

3. **Updated ClientProxyMode enum** (`Src/AppLib/VpnHood.AppLib.App/Settings/ClientProxyMode.cs`)
   - Added `Socks4`, `Http`, and `Https` options to match ProxyServerType

4. **Enhanced Unit Tests** (`Tests/VpnHood.Test/Tests/ProxyServerManagerTest.cs`)
   - Added tests for multiple proxy type support
   - Added tests to verify unsupported proxy types are handled properly
   - Maintained existing test coverage

### Architecture

The ProxyServerManager now uses a factory pattern approach where:
- Each proxy type is handled in a separate case in `ConnectToProxyAsync`
- SOCKS5 is fully implemented using the existing `Socks5ProxyClient`
- Other proxy types throw `NotSupportedException` with clear messages
- The architecture is ready for easy extension when other proxy implementations become available

### How to Add New Proxy Types

When proxy client implementations become available from VpnHood.Core.Proxies:

1. Add the necessary using statements:
   ```csharp
   using VpnHood.Core.Proxies.Socks4ProxyClients;
   using VpnHood.Core.Proxies.HttpProxyClients;
   ```

2. Update the `ConnectToProxyAsync` method to implement the new proxy types:
   ```csharp
   case ProxyServerType.Socks4:
       var socks4Options = new Socks4ProxyClientOptions
       {
           ProxyEndPoint = GetProxyEndPoint(proxyServerEndPoint),
           Username = proxyServerEndPoint.Username
       };
       var socks4Client = new Socks4ProxyClient(socks4Options);
       await socks4Client.ConnectAsync(tcpClient, destination, cancellationToken);
       break;
   ```

3. Update the `RemoveBadServers` method to support the new proxy types

### Current Status

- ? SOCKS5: Fully implemented and tested
- ?? SOCKS4: Architecture ready, implementation pending
- ?? HTTP: Architecture ready, implementation pending  
- ?? HTTPS: Architecture ready, implementation pending

The implementation maintains full backward compatibility while providing a clean extension point for future proxy types.