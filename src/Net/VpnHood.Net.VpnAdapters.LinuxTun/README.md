# VpnHood.Net.VpnAdapters.LinuxTun

The Linux VPN adapter of [VpnHood](https://github.com/vpnhood/VpnHood): a TUN device, a virtual network interface that
the host's traffic is routed into, whose IP packets a VPN reads and writes. It implements
`VpnHood.Net.VpnAdapters.Abstractions`.

```bash
dotnet add package VpnHood.Net.VpnAdapters.LinuxTun
```

## How it works

- `LinuxTunVpnAdapter` is an `IVpnAdapter`: make it with a `LinuxVpnAdapterSettings`, whose `AdapterName` names the
  interface (`LinuxTunVpnAdapter.GetValidAdapterName` derives a valid one from any name), and use it as the
  [Abstractions quick start](https://github.com/vpnhood/VpnHood/blob/develop/src/Net/VpnHood.Net.VpnAdapters.Abstractions/README.md)
  uses `NullVpnAdapter`.
- The TUN device is the process's own: the kernel removes it, with its routes, when the process ends, a crash
  included. Its alias carries a tag made from the settings' `AppId`, so a start can tell a device the app left, which
  it removes, from another app's interface of the same name, which refuses the start; an untagged TUN device of the
  name, as versions before tags made, is taken for the app's own.
- A start sets the interface's addresses and routes with `ip`, and its MTU when the options give one, turns on IPv4
  and IPv6 forwarding for the whole host, and tries to keep NetworkManager off the interface. Its DNS servers go to
  systemd-resolved, with the routing domain `~.`, which sends them every name no other link has a more specific
  domain for, or else to `resolvconf`; the start fails when `/etc/resolv.conf` would still name another server. While
  they are set, an nftables rule tries to send them the DNS queries over TCP or UDP port 53 that the host forwards,
  such as a container's, for the local network or a server systemd-resolved knows.
- For a server, `UseNat` masquerades the virtual networks, IPv4 and IPv6, out of the interface of the IPv4 default
  route with `iptables` and `ip6tables`. There is no app filter.
- Call `LinuxTunVpnAdapter.RemoveLeftovers` when the app starts: it removes the DNS settings a crashed run left.

## Requirements

- Linux, and root: the adapter runs `ip`, `sysctl`, `resolvectl` or `resolvconf`, `iptables`, `nft` and `udevadm`
  through bash.
- An IPv4 default route.
- systemd-resolved, with `/etc/resolv.conf` linked to it, or a `resolvconf` with an exclusive mode, as openresolv's,
  for the VPN's DNS servers; nftables for the DNS of containers; iptables for NAT.

## License

LGPL-2.1-only. See the [VpnHood repository](https://github.com/vpnhood/VpnHood).
