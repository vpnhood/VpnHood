using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Devices.WindowsTun.WinNative;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.VpnAdapters.WinTun;

namespace VpnHood.Core.Client.Devices.WindowsTun;

/// <summary>
/// Represents the kill switch's block on Windows: the Windows Filtering Platform's own filters, under a provider and a
/// sublayer of ours.
/// </summary>
/// <param name="owner">
/// The app's storage folder, which names the app whose block it is and an update keeps, where the exe's folder changes.
/// </param>
/// <param name="serviceName">
/// The service whose processes the block lets out; <see langword="null"/> in a run as the person, whose block lets out
/// none of its processes.
/// </param>
/// <remarks>
/// <para>
/// The filters are persistent: the Base Filtering Engine keeps them through a crash of the service and restores them
/// when it starts at boot, so the block holds through both, as a crash lifting it would defeat its purpose. They are
/// not boot-time filters, so they hold once the engine has started. Not a dynamic session's, which the engine deletes
/// with its process: WireGuard for Windows uses one, and its block goes with a crash.
/// </para>
/// <para>
/// The connect and accept layers (ALE) judge a flow at its start, and again where Windows reauthorizes it, nothing per
/// packet; the forward layers judge what
/// Windows routes itself, which would otherwise go around the block. They have no port field, so DNS routed to an
/// address of the split passes. A known limit: WinNAT's traffic — WSL2's NAT mode, so Docker Desktop's too — is seen by
/// no WFP layer (seen on Windows Server 2025 with WSL 3.0.3), so it goes into the tunnel while one is up and around the
/// block while none is. The Hyper-V firewall could stop it only while the service runs, never after a crash.
/// </para>
/// <para>
/// The keys are the SDK's, so every app built on VpnHood shares the one block; the provider's data names the app whose
/// block it is. It needs an administrator: the service, which runs as SYSTEM.
/// </para>
/// </remarks>
public sealed class WindowsVpnKillSwitch(string owner, string? serviceName) : IVpnKillSwitch
{
    private static readonly Guid ProviderKey = new("7e45054d-79d4-41a8-9bc4-f1409b25dcfe");
    private static readonly Guid SubLayerKey = new("552f1197-0054-4f0b-bc94-d9225d265b5b");

    // The filters' keys: this one with its last byte an index, so a removal finds every filter any build added.
    private static readonly Guid FilterKeyBase = new("146ff0ba-f091-48a7-b02e-585acdfbdb00");
    private const int MaxFilterCount = 64;

    // The service's permit: the service and every process it starts - the app, and its VPN host - by the SID Windows
    // puts in their tokens, the service's SID type being unrestricted ("service install" sets it). No other process
    // has it: not the window or a command, which run as the person, nor another SYSTEM process. Made from the name,
    // so both processes write the same filters (D: allow FWP_ACTRL_MATCH_FILTER and READ_CONTROL to the SID).
    private readonly string? _serviceUserSddl =
        serviceName is null ? null : $"O:SYD:(A;;CCRC;;;{GetServiceSid(serviceName)})";

    // The weights within our sublayer, the highest matching filter deciding.
    private const byte WeightService = 15;
    private const byte WeightTunnel = 14;
    private const byte WeightLoopback = 13;
    private const byte WeightDhcpNdp = 12;
    private const byte WeightDnsServers = 11;
    private const byte WeightBlockDns = 10;
    private const byte WeightPassIpRanges = 9;
    private const byte WeightBlockAll = 0;

    private const byte ProtocolTcp = 6;
    private const byte ProtocolUdp = 17;
    private const byte ProtocolIcmpV6 = 58;
    private const ushort DnsPort = 53;

    // one change at a time in this process; the engine's transaction keeps other processes' apart
    private static readonly Lock ChangeLock = new();

    /// <inheritdoc/>
    /// <remarks>It runs at once; the token is not watched.</remarks>
    /// <exception cref="Win32Exception">The filtering engine could not be opened or read.</exception>
    public Task<bool> IsBlocking(CancellationToken cancellationToken)
    {
        lock (ChangeLock) {
            var engine = OpenEngine();
            try {
                return Task.FromResult(GetOwner(engine) == owner);
            }
            finally {
                WfpNative.FwpmEngineClose0(engine);
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>It runs at once, in one transaction of the filtering engine; the token is not watched.</remarks>
    /// <exception cref="Win32Exception">The filtering engine refused a change, as without an administrator.</exception>
    public Task Block(VpnKillSwitchRules rules, CancellationToken cancellationToken)
    {
        lock (ChangeLock) {
            var engine = OpenEngine();
            try {
                using var native = new NativeMemory();
                var tunnelLuid = GetTunnelLuid(rules.AdapterName);
                var filters = CreateFilters(rules, tunnelLuid, native);
                InTransaction(engine, () => {
                    Delete(engine);
                    // ReSharper disable once AccessToDisposedClosure
                    Add(engine, filters, native);
                });

                VhLogger.Instance.LogInformation(
                    "The kill switch's block is up. Filters: {Filters}, Tunnel: {Tunnel}, PassIpRanges: {PassIpRanges}",
                    filters.Count, tunnelLuid != null, rules.PassIpRanges.Count);
            }
            finally {
                WfpNative.FwpmEngineClose0(engine);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>It runs at once, in one transaction of the filtering engine; the token is not watched.</remarks>
    /// <exception cref="Win32Exception">The filtering engine refused a change, as without an administrator.</exception>
    public Task Unblock(bool anyApp, CancellationToken cancellationToken)
    {
        lock (ChangeLock) {
            var engine = OpenEngine();
            try {
                var blockOwner = GetOwner(engine);
                if (blockOwner == null || (!anyApp && blockOwner != owner))
                    return Task.CompletedTask;

                InTransaction(engine, () => Delete(engine));
                VhLogger.Instance.LogInformation("The kill switch's block is lifted. Its app: {Owner}",
                    blockOwner == owner ? "this" : "another");
            }
            finally {
                WfpNative.FwpmEngineClose0(engine);
            }
        }

        return Task.CompletedTask;
    }

    // A session of its own, not dynamic, so what it adds outlives it.
    private static IntPtr OpenEngine()
    {
        var session = new WfpNative.FwpmSession { TxnWaitTimeoutInMSec = 10_000 };
        Check(WfpNative.FwpmEngineOpen0(null, WfpNative.RpcAuthnDefault, IntPtr.Zero, ref session, out var engine),
            "open the Windows Filtering Platform");
        return engine;
    }

    // One change with no gap: a filter re-pointed is never missing meanwhile.
    private static void InTransaction(IntPtr engine, Action action)
    {
        Check(WfpNative.FwpmTransactionBegin0(engine, 0), "begin a WFP transaction");
        try {
            action();
            Check(WfpNative.FwpmTransactionCommit0(engine), "commit a WFP transaction");
        }
        catch {
            WfpNative.FwpmTransactionAbort0(engine);
            throw;
        }
    }

    // The app whose block is up, or null where none is.
    private static string? GetOwner(IntPtr engine)
    {
        var key = ProviderKey;
        var error = WfpNative.FwpmProviderGetByKey0(engine, ref key, out var providerPtr);
        if (error == WfpNative.ErrorProviderNotFound)
            return null;

        Check(error, "read the kill switch's provider");
        try {
            var provider = Marshal.PtrToStructure<WfpNative.FwpmProvider>(providerPtr);
            return provider.ProviderData.Data == IntPtr.Zero
                ? string.Empty
                : Marshal.PtrToStringUni(provider.ProviderData.Data, (int)provider.ProviderData.Size / 2);
        }
        finally {
            WfpNative.FwpmFreeMemory0(ref providerPtr);
        }
    }

    // Every filter any build added, then the sublayer and the provider; what is not there is no failure.
    private static void Delete(IntPtr engine)
    {
        for (var i = 0; i < MaxFilterCount; i++) {
            var filterKey = GetFilterKey(i);
            CheckDeleted(WfpNative.FwpmFilterDeleteByKey0(engine, ref filterKey), WfpNative.ErrorFilterNotFound,
                "delete a kill switch filter");
        }

        var subLayerKey = SubLayerKey;
        CheckDeleted(WfpNative.FwpmSubLayerDeleteByKey0(engine, ref subLayerKey), WfpNative.ErrorSubLayerNotFound,
            "delete the kill switch's sublayer");

        var providerKey = ProviderKey;
        CheckDeleted(WfpNative.FwpmProviderDeleteByKey0(engine, ref providerKey), WfpNative.ErrorProviderNotFound,
            "delete the kill switch's provider");
    }

    private void Add(IntPtr engine, IReadOnlyList<FilterSpec> filters, NativeMemory native)
    {
        var ownerBytes = Encoding.Unicode.GetBytes(owner);
        var provider = new WfpNative.FwpmProvider {
            ProviderKey = ProviderKey,
            DisplayData = native.DisplayData("VpnHood Kill Switch", "Nothing leaves outside the VPN tunnel."),
            Flags = WfpNative.ProviderFlagPersistent,
            ProviderData = new WfpNative.FwpByteBlob { Size = (uint)ownerBytes.Length, Data = native.Bytes(ownerBytes) }
        };
        Check(WfpNative.FwpmProviderAdd0(engine, ref provider, IntPtr.Zero), "add the kill switch's provider");

        var subLayer = new WfpNative.FwpmSubLayer {
            SubLayerKey = SubLayerKey,
            DisplayData = native.DisplayData("VpnHood Kill Switch", null),
            Flags = WfpNative.SubLayerFlagPersistent,
            ProviderKey = native.Struct(ProviderKey),
            Weight = ushort.MaxValue
        };
        Check(WfpNative.FwpmSubLayerAdd0(engine, ref subLayer, IntPtr.Zero), "add the kill switch's sublayer");

        for (var i = 0; i < filters.Count; i++) {
            var spec = filters[i];
            var filter = new WfpNative.FwpmFilter {
                FilterKey = GetFilterKey(i),
                DisplayData = native.DisplayData("VpnHood Kill Switch: " + spec.Name, null),
                Flags = WfpNative.FilterFlagPersistent,
                ProviderKey = native.Struct(ProviderKey),
                LayerKey = spec.LayerKey,
                SubLayerKey = SubLayerKey,
                Weight = new WfpNative.FwpValue { Type = WfpNative.DataTypeUInt8, Value = spec.Weight },
                NumFilterConditions = (uint)spec.Conditions.Count,
                FilterCondition = native.Array(spec.Conditions),
                Action = new WfpNative.FwpmAction { Type = spec.IsPermit ? WfpNative.ActionPermit : WfpNative.ActionBlock }
            };
            Check(WfpNative.FwpmFilterAdd0(engine, ref filter, IntPtr.Zero, out _), $"add the kill switch filter {spec.Name}");
        }
    }

    // The block's filters, by the weights above; a family's filter that would have no address left goes.
    private List<FilterSpec> CreateFilters(VpnKillSwitchRules rules, ulong? tunnelLuid, NativeMemory native)
    {
        var serviceUser = _serviceUserSddl is null ? IntPtr.Zero : native.SecurityDescriptor(_serviceUserSddl);
        var filters = new List<FilterSpec>();
        foreach (var addressFamily in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 }) {
            var isV6 = addressFamily == AddressFamily.InterNetworkV6;
            var familyName = isV6 ? "IPv6" : "IPv4";
            var passIpRanges = rules.PassIpRanges.Where(x => x.AddressFamily == addressFamily).ToArray();
            var dnsServers = rules.DnsServers.Where(x => x.AddressFamily == addressFamily).ToArray();
            var dhcpPorts = isV6 ? ((ushort)546, (ushort)547) : ((ushort)68, (ushort)67);

            // a connection made from here, and one accepted here
            foreach (var isOutbound in new[] { true, false }) {
                var layerKey = (isOutbound, isV6) switch {
                    (true, false) => WfpNative.LayerAleAuthConnectV4,
                    (true, true) => WfpNative.LayerAleAuthConnectV6,
                    (false, false) => WfpNative.LayerAleAuthRecvAcceptV4,
                    (false, true) => WfpNative.LayerAleAuthRecvAcceptV6
                };
                var name = $"{(isOutbound ? "out" : "in")} {familyName}";

                if (serviceUser != IntPtr.Zero)
                    filters.Add(new FilterSpec($"the service, {name}", layerKey, WeightService, true, [
                        NativeMemory.SecurityDescriptorCondition(WfpNative.ConditionAleUserId, serviceUser)
                    ]));

                if (tunnelLuid != null)
                    filters.Add(new FilterSpec($"the tunnel, {name}", layerKey, WeightTunnel, true, [
                        native.UInt64Condition(WfpNative.ConditionIpLocalInterface, tunnelLuid.Value)
                    ]));

                filters.Add(new FilterSpec($"loopback, {name}", layerKey, WeightLoopback, true, [
                    FlagsCondition(WfpNative.ConditionFlagIsLoopback)
                ]));

                // DHCP, so the machine keeps its address
                filters.Add(new FilterSpec($"DHCP, {name}", layerKey, WeightDhcpNdp, true, [
                    UInt8Condition(WfpNative.ConditionIpProtocol, ProtocolUdp),
                    UInt16Condition(WfpNative.ConditionIpLocalPort, dhcpPorts.Item1),
                    UInt16Condition(WfpNative.ConditionIpRemotePort, dhcpPorts.Item2)
                ]));

                // NDP: router and neighbor solicitations and advertisements, and redirects (ICMPv6 133-137)
                if (isV6)
                    filters.Add(new FilterSpec($"NDP, {name}", layerKey, WeightDhcpNdp, true, [
                        UInt8Condition(WfpNative.ConditionIpProtocol, ProtocolIcmpV6),
                        UInt16Condition(WfpNative.ConditionIpLocalPort, 133),
                        UInt16Condition(WfpNative.ConditionIpLocalPort, 134),
                        UInt16Condition(WfpNative.ConditionIpLocalPort, 135),
                        UInt16Condition(WfpNative.ConditionIpLocalPort, 136),
                        UInt16Condition(WfpNative.ConditionIpLocalPort, 137)
                    ]));

                // DNS outside the tunnel goes only to an out-of-tunnel plan's resolvers, never to a LAN router,
                // which would otherwise learn every name
                if (isOutbound) {
                    if (dnsServers.Length > 0)
                        filters.Add(new FilterSpec($"DNS servers, {name}", layerKey, WeightDnsServers, true, [
                            UInt16Condition(WfpNative.ConditionIpRemotePort, DnsPort),
                            ..dnsServers.Select(x => native.RangeCondition(WfpNative.ConditionIpRemoteAddress, new IpRange(x)))
                        ]));

                    filters.Add(new FilterSpec($"no DNS, {name}", layerKey, WeightBlockDns, false, [
                        UInt16Condition(WfpNative.ConditionIpRemotePort, DnsPort)
                    ]));
                }

                // the device-level split: one filter per family, its addresses ORed
                if (passIpRanges.Length > 0)
                    filters.Add(new FilterSpec($"the split, {name}", layerKey, WeightPassIpRanges, true,
                        passIpRanges.Select(x => native.RangeCondition(WfpNative.ConditionIpRemoteAddress, x)).ToArray()));

                filters.Add(new FilterSpec($"the rest, {name}", layerKey, WeightBlockAll, false, []));
            }

            // what Windows forwards: into the tunnel and out of it, or to the split's addresses
            var forwardLayerKey = isV6 ? WfpNative.LayerIpForwardV6 : WfpNative.LayerIpForwardV4;
            var forwardName = $"forwarded {familyName}";
            if (tunnelLuid != null) {
                filters.Add(new FilterSpec($"into the tunnel, {forwardName}", forwardLayerKey, WeightTunnel, true, [
                    native.UInt64Condition(WfpNative.ConditionIpForwardInterface, tunnelLuid.Value)
                ]));
                filters.Add(new FilterSpec($"from the tunnel, {forwardName}", forwardLayerKey, WeightTunnel, true, [
                    native.UInt64Condition(WfpNative.ConditionIpLocalInterface, tunnelLuid.Value)
                ]));
            }

            if (passIpRanges.Length > 0)
                filters.Add(new FilterSpec($"the split, {forwardName}", forwardLayerKey, WeightPassIpRanges, true,
                    passIpRanges.Select(x => native.RangeCondition(WfpNative.ConditionIpDestinationAddress, x)).ToArray()));

            filters.Add(new FilterSpec($"the rest, {forwardName}", forwardLayerKey, WeightBlockAll, false, []));
        }

        if (filters.Count > MaxFilterCount)
            throw new InvalidOperationException($"The kill switch has more filters than it can remove: {filters.Count}.");

        return filters;
    }

    // The tunnel's interface, where its adapter is up: none before the first start, when only the service passes.
    private static ulong? GetTunnelLuid(string adapterName)
    {
        var adapterGuid = WinTunVpnAdapter.BuildGuidFromName(adapterName);
        return WfpNative.ConvertInterfaceGuidToLuid(ref adapterGuid, out var luid) == 0 ? luid : null;
    }

    // A service's SID as Windows makes it from the name (sc showsid): SHA-1 of the name in capitals, UTF-16, read
    // as five little-endian numbers after S-1-5-80. Made here rather than looked up, since a run as the person, or a
    // service not yet registered, has nothing to look up.
    private static string GetServiceSid(string serviceName)
    {
        var hash = SHA1.HashData(Encoding.Unicode.GetBytes(serviceName.ToUpperInvariant()));
        var subAuthorities = Enumerable.Range(0, 5)
            .Select(i => BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(i * 4)))
            .ToArray();
        return "S-1-5-80-" + string.Join('-', subAuthorities);
    }

    private static Guid GetFilterKey(int index)
    {
        var bytes = FilterKeyBase.ToByteArray();
        bytes[15] = (byte)index;
        return new Guid(bytes);
    }

    private static WfpNative.FwpmFilterCondition UInt8Condition(Guid fieldKey, byte value)
    {
        return new WfpNative.FwpmFilterCondition {
            FieldKey = fieldKey,
            MatchType = WfpNative.MatchEqual,
            ConditionValue = new WfpNative.FwpValue { Type = WfpNative.DataTypeUInt8, Value = value }
        };
    }

    private static WfpNative.FwpmFilterCondition UInt16Condition(Guid fieldKey, ushort value)
    {
        return new WfpNative.FwpmFilterCondition {
            FieldKey = fieldKey,
            MatchType = WfpNative.MatchEqual,
            ConditionValue = new WfpNative.FwpValue { Type = WfpNative.DataTypeUInt16, Value = value }
        };
    }

    private static WfpNative.FwpmFilterCondition FlagsCondition(uint flags)
    {
        return new WfpNative.FwpmFilterCondition {
            FieldKey = WfpNative.ConditionFlags,
            MatchType = WfpNative.MatchFlagsAllSet,
            ConditionValue = new WfpNative.FwpValue { Type = WfpNative.DataTypeUInt32, Value = (nint)flags }
        };
    }

    private static void Check(uint error, string action)
    {
        if (error != 0)
            throw new Win32Exception(unchecked((int)error), $"Could not {action}. Error: 0x{error:X8}");
    }

    private static void CheckDeleted(uint error, uint notFoundError, string action)
    {
        if (error != notFoundError)
            Check(error, action);
    }

    private sealed record FilterSpec(
        string Name,
        Guid LayerKey,
        byte Weight,
        bool IsPermit,
        IReadOnlyList<WfpNative.FwpmFilterCondition> Conditions);

    // What the filters point at, unmanaged until the change is made: WFP copies it all.
    private sealed class NativeMemory : IDisposable
    {
        private readonly List<IntPtr> _hGlobals = [];
        private readonly List<IntPtr> _localAllocations = [];

        public IntPtr Bytes(byte[] bytes)
        {
            var ptr = Alloc(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return ptr;
        }

        public IntPtr Struct<T>(T value) where T : struct
        {
            var ptr = Alloc(Marshal.SizeOf<T>());
            Marshal.StructureToPtr(value, ptr, false);
            return ptr;
        }

        public IntPtr Array<T>(IReadOnlyList<T> values) where T : struct
        {
            if (values.Count == 0)
                return IntPtr.Zero;

            var size = Marshal.SizeOf<T>();
            var ptr = Alloc(size * values.Count);
            for (var i = 0; i < values.Count; i++)
                Marshal.StructureToPtr(values[i], ptr + i * size, false);
            return ptr;
        }

        public WfpNative.FwpmDisplayData DisplayData(string name, string? description)
        {
            return new WfpNative.FwpmDisplayData {
                Name = String(name),
                Description = description == null ? IntPtr.Zero : String(description)
            };
        }

        public IntPtr SecurityDescriptor(string sddl)
        {
            if (!WfpNative.ConvertStringSecurityDescriptorToSecurityDescriptor(sddl, 1, out var sd, out var size))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not make the service's user condition.");

            _localAllocations.Add(sd);
            return Struct(new WfpNative.FwpByteBlob { Size = size, Data = sd });
        }

        public static WfpNative.FwpmFilterCondition SecurityDescriptorCondition(Guid fieldKey, IntPtr sdBlob)
        {
            return new WfpNative.FwpmFilterCondition {
                FieldKey = fieldKey,
                MatchType = WfpNative.MatchEqual,
                ConditionValue = new WfpNative.FwpValue { Type = WfpNative.DataTypeSecurityDescriptor, Value = sdBlob }
            };
        }

        public WfpNative.FwpmFilterCondition UInt64Condition(Guid fieldKey, ulong value)
        {
            return new WfpNative.FwpmFilterCondition {
                FieldKey = fieldKey,
                MatchType = WfpNative.MatchEqual,
                ConditionValue = new WfpNative.FwpValue { Type = WfpNative.DataTypeUInt64, Value = Struct(value) }
            };
        }

        // an address range: IPv4 as a host-order UINT32, IPv6 as its 16 bytes
        public WfpNative.FwpmFilterCondition RangeCondition(Guid fieldKey, IpRange ipRange)
        {
            var range = new WfpNative.FwpRange {
                ValueLow = Address(ipRange.FirstIpAddress),
                ValueHigh = Address(ipRange.LastIpAddress)
            };
            return new WfpNative.FwpmFilterCondition {
                FieldKey = fieldKey,
                MatchType = WfpNative.MatchRange,
                ConditionValue = new WfpNative.FwpValue { Type = WfpNative.DataTypeRange, Value = Struct(range) }
            };
        }

        private WfpNative.FwpValue Address(IPAddress ipAddress)
        {
            var bytes = ipAddress.GetAddressBytes();
            return ipAddress.AddressFamily == AddressFamily.InterNetwork
                ? new WfpNative.FwpValue {
                    Type = WfpNative.DataTypeUInt32,
                    Value = (nint)((uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3])
                }
                : new WfpNative.FwpValue { Type = WfpNative.DataTypeByteArray16, Value = Bytes(bytes) };
        }

        private IntPtr String(string value)
        {
            var ptr = Marshal.StringToHGlobalUni(value);
            _hGlobals.Add(ptr);
            return ptr;
        }

        private IntPtr Alloc(int size)
        {
            var ptr = Marshal.AllocHGlobal(size);
            _hGlobals.Add(ptr);
            return ptr;
        }

        public void Dispose()
        {
            foreach (var ptr in _hGlobals)
                Marshal.FreeHGlobal(ptr);

            foreach (var ptr in _localAllocations)
                WfpNative.LocalFree(ptr);
        }
    }
}
