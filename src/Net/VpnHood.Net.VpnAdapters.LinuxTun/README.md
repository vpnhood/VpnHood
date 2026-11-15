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
  name, as versions before tags made, is taken for the app's own while it is up.
- A start sets the interface's addresses and routes with `ip`, and its MTU when the options give one, and tries to
  keep NetworkManager off the interface. Its DNS servers go to
  systemd-resolved, with the routing domain `~.`, which sends them every name no other link has a more specific
  domain for, or else to `resolvconf`; the start fails when `/etc/resolv.conf` would still name another server. While
  they are set, an nftables rule tries to send them the DNS queries over TCP or UDP port 53 that the host forwards,
  such as a container's, for the local network or a server systemd-resolved knows.
- systemd-resolved sends a name under an interface's own DNS domain, as a network's DHCP gives one, to that
  interface's servers, whatever the routes say. With `VpnAdapterOptions.UseExclusiveDns` the adapter takes the DNS
  domains off the other interfaces while it is up and gives them back when it stops, so those names are asked of
  the VPN's servers too. A start fails where an interface still has a domain after that: for one that
  systemd-networkd manages, systemd 245 and 249 (Ubuntu 20.04 and 22.04) leave it there, and 255 and later take it
  off. The adapter does the same again after a change of the host's network, and stops if it fails there; a domain
  a network sets later without such a change stays until the next start.
- For a server, `UseNat` masquerades each virtual network, IPv4 and IPv6, out of the interface of the default route
  of its IP version, with `iptables` and `ip6tables` rules in chains of the adapter's own (`vpnhood_nat_` and its
  name), and turns on forwarding for the whole host, for the IP versions it translates. For IPv6 it first sets
  `accept_ra` to 2 on every interface that takes router advertisements now (one that does not forward yet, with
  `accept_ra` at 1), other than the adapter's own, so the host keeps the IPv6 routes it has from them. A start with
  `UseNat` removes the chains an earlier run left, as after a crash, of both IP versions. There is no app filter.
- Call `LinuxTunVpnAdapter.RemoveLeftovers` when the app starts: it removes the DNS settings a crashed run left,
  and gives the other interfaces their DNS domains back.

## Requirements

- Linux, and root: the adapter runs `ip`, `sysctl`, `resolvectl` or `resolvconf`, `iptables`, `nft` and `udevadm`
  through bash.
- For NAT, a default route of each IP version it translates.
- systemd-resolved, with `/etc/resolv.conf` linked to it, or a `resolvconf` with an exclusive mode, as openresolv's,
  for the VPN's DNS servers; nftables for the DNS of containers; iptables for NAT.

## License

LGPL-2.1-only. See the [VpnHood repository](https://github.com/vpnhood/VpnHood).
