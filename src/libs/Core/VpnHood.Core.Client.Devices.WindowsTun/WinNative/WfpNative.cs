using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming
// ReSharper disable IdentifierTypo
namespace VpnHood.Core.Client.Devices.WindowsTun.WinNative;

// The Windows Filtering Platform's management API (fwpuclnt.dll), as much as the kill switch's block uses.
// The structs follow fwpmtypes.h and fwptypes.h field by field; a union is a pointer-sized IntPtr, which
// holds a small value itself and points at a larger one.
internal static class WfpNative
{
    public const uint RpcAuthnDefault = 0xFFFFFFFF;
    public const uint ProviderFlagPersistent = 0x1;
    public const uint SubLayerFlagPersistent = 0x1;
    public const uint FilterFlagPersistent = 0x1;
    public const uint ActionBlock = 0x1 | 0x1000;
    public const uint ActionPermit = 0x2 | 0x1000;
    public const uint ConditionFlagIsLoopback = 0x1;

    // FWP_E_*
    public const uint ErrorFilterNotFound = 0x80320003;
    public const uint ErrorProviderNotFound = 0x80320005;
    public const uint ErrorSubLayerNotFound = 0x80320007;

    // FWP_DATA_TYPE
    public const uint DataTypeUInt8 = 1;
    public const uint DataTypeUInt16 = 2;
    public const uint DataTypeUInt32 = 3;
    public const uint DataTypeUInt64 = 4;
    public const uint DataTypeByteArray16 = 11;
    public const uint DataTypeSecurityDescriptor = 14;
    public const uint DataTypeRange = 258;

    // FWP_MATCH_TYPE
    public const uint MatchEqual = 0;
    public const uint MatchRange = 5;
    public const uint MatchFlagsAllSet = 6;

    public static readonly Guid LayerAleAuthConnectV4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    public static readonly Guid LayerAleAuthConnectV6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");
    public static readonly Guid LayerAleAuthRecvAcceptV4 = new("e1cd9fe7-f4b5-4273-96c0-592e487b8650");
    public static readonly Guid LayerAleAuthRecvAcceptV6 = new("a3b42c97-9f04-4672-b87e-cee9c483257f");
    public static readonly Guid LayerIpForwardV4 = new("a82acc24-4ee1-4ee1-b465-fd1d25cb10a4");
    public static readonly Guid LayerIpForwardV6 = new("7b964818-19c7-493a-b71f-832c3684d28c");

    public static readonly Guid ConditionAleUserId = new("af043a0a-b34d-4f86-979c-c90371af6e66");
    public static readonly Guid ConditionIpLocalInterface = new("4cd62a49-59c3-4969-b7f3-bda5d32890a4");
    public static readonly Guid ConditionIpForwardInterface = new("1076b8a5-6323-4c5e-9810-e8d3fc9e6136");
    public static readonly Guid ConditionIpRemoteAddress = new("b235ae9a-1d64-49b8-a44c-5ff3d9095045");
    public static readonly Guid ConditionIpDestinationAddress = new("2d79133b-b390-45c6-8699-acaceaafed33");
    public static readonly Guid ConditionIpRemotePort = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");
    public static readonly Guid ConditionIpLocalPort = new("0c1ba1af-5765-453f-af22-a8f791ac775b"); // ICMP's type too
    public static readonly Guid ConditionIpProtocol = new("3971ef2b-623e-4f9a-8cb1-6e79b806b9a7");
    public static readonly Guid ConditionFlags = new("632ce23b-5167-435c-86d7-e903684aa80c");

    [StructLayout(LayoutKind.Sequential)]
    public struct FwpByteBlob
    {
        public uint Size;
        public IntPtr Data;
    }

    // FWP_VALUE0 and FWP_CONDITION_VALUE0
    [StructLayout(LayoutKind.Sequential)]
    public struct FwpValue
    {
        public uint Type;
        public IntPtr Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FwpRange
    {
        public FwpValue ValueLow;
        public FwpValue ValueHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FwpmDisplayData
    {
        public IntPtr Name;
        public IntPtr Description;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FwpmSession
    {
        public Guid SessionKey;
        public FwpmDisplayData DisplayData;
        public uint Flags;
        public uint TxnWaitTimeoutInMSec;
        public uint ProcessId;
        public IntPtr Sid;
        public IntPtr Username;
        public int KernelMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FwpmProvider
    {
        public Guid ProviderKey;
        public FwpmDisplayData DisplayData;
        public uint Flags;
        public FwpByteBlob ProviderData;
        public IntPtr ServiceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FwpmSubLayer
    {
        public Guid SubLayerKey;
        public FwpmDisplayData DisplayData;
        public uint Flags;
        public IntPtr ProviderKey;
        public FwpByteBlob ProviderData;
        public ushort Weight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FwpmFilterCondition
    {
        public Guid FieldKey;
        public uint MatchType;
        public FwpValue ConditionValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FwpmAction
    {
        public uint Type;
        public Guid FilterType;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FwpmFilter
    {
        public Guid FilterKey;
        public FwpmDisplayData DisplayData;
        public uint Flags;
        public IntPtr ProviderKey;
        public FwpByteBlob ProviderData;
        public Guid LayerKey;
        public Guid SubLayerKey;
        public FwpValue Weight;
        public uint NumFilterConditions;
        public IntPtr FilterCondition;
        public FwpmAction Action;

        // union { UINT64 rawContext; GUID providerContextKey; }, aligned as its UINT64
        public ulong RawContext;
        public ulong RawContextHigh;
        public IntPtr Reserved;
        public ulong FilterId;
        public FwpValue EffectiveWeight;
    }

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmEngineOpen0(string? serverName, uint authnService, IntPtr authIdentity,
        ref FwpmSession session, out IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmEngineClose0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmTransactionBegin0(IntPtr engineHandle, uint flags);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmTransactionCommit0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmTransactionAbort0(IntPtr engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmProviderAdd0(IntPtr engineHandle, ref FwpmProvider provider, IntPtr sd);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmProviderGetByKey0(IntPtr engineHandle, ref Guid key, out IntPtr provider);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmProviderDeleteByKey0(IntPtr engineHandle, ref Guid key);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmSubLayerAdd0(IntPtr engineHandle, ref FwpmSubLayer subLayer, IntPtr sd);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmSubLayerDeleteByKey0(IntPtr engineHandle, ref Guid key);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmFilterAdd0(IntPtr engineHandle, ref FwpmFilter filter, IntPtr sd, out ulong id);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmFilterDeleteByKey0(IntPtr engineHandle, ref Guid key);

    [DllImport("fwpuclnt.dll")]
    public static extern void FwpmFreeMemory0(ref IntPtr p);

    [DllImport("iphlpapi.dll")]
    public static extern int ConvertInterfaceGuidToLuid(ref Guid interfaceGuid, out ulong interfaceLuid);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string stringSecurityDescriptor,
        uint stringSdRevision, out IntPtr securityDescriptor, out uint securityDescriptorSize);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr hMem);
}
