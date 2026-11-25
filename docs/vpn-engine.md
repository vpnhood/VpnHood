# How the VPN Engine Works

The VPN engine runs in the VPN service ([the two halves](client-architecture.md#the-two-halves)). It takes
every packet the device's apps send, decides where each one goes, and carries it to the VPN server in a
tunnel. This page shows how the engine gets its adapter and starts, then how a packet travels between the
adapter and the tunnel.

## How the engine gets its adapter

```mermaid
sequenceDiagram
    autonumber
    participant Host as VpnServiceHost<br/>in the platform's VPN service
    participant Adapter as IVpnAdapter
    participant Client as VpnHoodClient
    participant Session as ClientSession
    participant Server as VpnHood server

    Host->>Adapter: CreateAdapter()<br/>Android, iOS, WinTun,<br/>Linux TUN, Windows VPN plug-in
    Host->>Client: new VpnHoodClient(adapter, socketFactory, ...)
    Note over Client: wraps the socket factory: every socket it makes<br/>calls adapter.ProtectSocket, so its own traffic<br/>to the server never loops back into the TUN
    Client->>Server: hello (ClientSessionBuilder)
    Server-->>Client: session id and key, what the server carries
    Client->>Session: new ClientSession(adapter, ...)
    Note over Session: subscribes to adapter.PacketReceived<br/>and makes Tunnel, ClientTcpHost,<br/>ForwardingManager, ClientPacketHandler
    Session->>Session: ClientTcpHost.Start(), the local TCP stack
    Session->>Server: first packet channel: UDP socket,<br/>or TcpPacketChannelRequest
    Session->>Adapter: Start(options)<br/>interface, addresses, routes, DNS, app filter
    Adapter-->>Session: PacketReceived, for each packet the apps send
```

1. **The engine never makes the adapter.** The platform's VPN service makes it and hands it to
   `VpnHoodClient`. To the engine an adapter is a packet pipe: `PacketReceived` brings each packet the apps
   send, and `SendPacketQueued` writes the replies back. So one engine runs on every platform, and the tests
   run it on a `NullVpnAdapter`.
2. **The engine's own traffic stays out of the adapter.** The client wraps the socket factory, so each
   socket it opens calls the adapter's `ProtectSocket`, and its traffic to the server never loops back into
   the tunnel.
3. **The adapter starts last**, once the session has its local TCP stack running and its first packet
   channel to the server.

## How a packet travels

```mermaid
flowchart TB
    APPS["Apps on the device"] -->|"IP packets, by the OS routes"| AD["IVpnAdapter, the TUN"]
    AD -->|"PacketReceived"| PH{"ClientPacketHandler<br/>domain and IP filters,<br/>include or exclude"}

    PH -->|"blocked, or DropUdp, DropQuic"| DROP(["dropped"])
    PH -->|"included UDP and ping,<br/>TCP when TCP forwarding is off"| TQ
    PH -->|"TCP: forwarding on, or excluded"| TCPHOST["ClientTcpHost<br/>LocalTcpStack, VpnHood's own TCP stack,<br/>ends the app's TCP connection"]
    PH -->|"excluded UDP"| FWD["ForwardingManager<br/>UDP forwarder: a protected socket per flow,<br/>outside the tunnel"]

    TCPHOST -->|"each new connection"| SH{"ClientStreamHandler<br/>SNI and IP filters"}
    SH -->|"excluded: connects to the host itself"| FWD
    SH -->|"included: TcpForwardingChannelRequest<br/>with the first bytes"| SFC

    subgraph TUNNEL["Tunnel (ThrottledTunnel under a speed limit)"]
        TQ["SendPacketQueued<br/>a packet over the MTU gets ICMP Packet Too Big"]
        TQ -->|"UseUdp"| UDPCH["UDP packet channel<br/>datagrams sealed with the session key"]
        TQ -->|"otherwise, by source port"| SPC["Stream packet channels 1..N<br/>packets back to back on a stream"]
        SFC["Stream forwarding channels<br/>one per app TCP connection, bytes only"]
    end

    SPC --> CONN["ConnectorService<br/>TCP + TLS, or QUIC streams,<br/>HTTP or WebSocket framing"]
    SFC --> CONN
    CONN --> SRV
    UDPCH --> SRV

    subgraph SERVER["VpnHood server"]
        SRV["ServerHost, the session's Tunnel<br/>source must be the client's virtual IP,<br/>filters, limits"]
        SRV -->|"every packet of an IP version<br/>its NAT translates"| STUN["TUN adapter with NAT<br/>needs admin or root"]
        SRV -->|"UDP and ping,<br/>without the adapter"| SFWD["ForwardingManager<br/>UDP forwarder: a socket per flow,<br/>ping forwarder"]
        SRV -->|"TCP forwarding"| SCONN["its own TCP connection<br/>to the destination"]
    end

    STUN --> NET(("Internet"))
    SFWD --> NET
    SCONN --> NET
    FWD --> NET

    TUNNEL -.->|"Tunnel.PacketReceived"| BACK
    TCPHOST -.->|"replies the TCP stack builds"| BACK
    FWD -.->|"replies"| BACK
    BACK["ProcessIncomingPacket"] -.->|"SendPacketQueued"| AD

    classDef admin fill:#fff4e5,stroke:#d9480f,stroke-width:2px,stroke-dasharray: 5 3,color:#5c2400
    class STUN admin
```

1. **`ClientPacketHandler` sends each packet one of three ways**: into the tunnel, to the local TCP stack,
   or around the tunnel through `ForwardingManager`, for what split tunneling excludes; its UDP forwarder
   gives each excluded UDP flow a socket of its own. DNS goes through the tunnel by default, and pings always
   do.
2. **TCP takes one of two routes.** It goes as packets in the tunnel when the server's adapter can NAT its
   IP version. Otherwise it goes by TCP forwarding: the engine's own TCP stack ends the app's connection,
   only its bytes cross the tunnel, and the server opens its own connection to the destination. The domain
   filter always uses forwarding, as it reads the server name (SNI) in each connection's TLS hello.
3. **Only the server's TUN adapter needs admin or root** (marked in the drawing): it makes a network
   interface and the NAT that translates the clients' packets. A server without it still serves every app:
   its UDP forwarder sends UDP through a socket for each flow, its ping forwarder sends the pings, and TCP
   goes by TCP forwarding.
4. **The channel protocol picks the tunnel's transport**: one UDP channel, or stream channels over TLS or
   QUIC. A packet's channel follows its source port, so each flow stays on one channel, in order.
5. **Replies come back three ways**, from the tunnel, the TCP stack and the bypass, and all go back into the
   adapter.

## See also

- [VpnHood Client Architecture](client-architecture.md) — the layers around the engine, and the VPN service it runs in
- [How a VpnHood App Starts](app-startup.md) — what starts the app, and with it the VPN service
- [What Happens on Connect](connect.md) — the engine's part in a connect: finding a server, the hello, the adapter
