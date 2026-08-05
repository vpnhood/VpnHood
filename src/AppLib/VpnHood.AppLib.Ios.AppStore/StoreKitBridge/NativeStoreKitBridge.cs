using System.Runtime.InteropServices;
using System.Text.Json;
using ObjCRuntime;

namespace VpnHood.AppLib.Ios.AppStore.StoreKitBridge;

/// <summary>
/// Binding to the VpnHoodStoreKit Swift facade (swift/VpnHoodStoreKit): plain
/// C functions taking/returning JSON strings, completion via a C callback.
/// Microsoft.iOS cannot call SK2's Swift-async API directly — this facade is
/// the documented seam (see swift/README.md for building the xcframework).
/// </summary>
public class NativeStoreKitBridge : IStoreKitBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<StoreKitProduct>> LoadProducts(IReadOnlyList<string> productIds,
        CancellationToken cancellationToken)
    {
        var productIdsJson = JsonSerializer.Serialize(productIds, JsonOptions);
        var resultJson = await Invoke(
            contextHandle => vhsk_load_products(productIdsJson, contextHandle, CompletedDelegate),
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<List<StoreKitProduct>>(resultJson, JsonOptions)
            ?? throw new InvalidOperationException("StoreKit returned no product list.");
    }

    public async Task<StoreKitPurchase> Purchase(string productId, Guid appAccountToken,
        CancellationToken cancellationToken)
    {
        var resultJson = await Invoke(
            contextHandle => vhsk_purchase(productId, appAccountToken.ToString(), contextHandle, CompletedDelegate),
            cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<StoreKitPurchase>(resultJson, JsonOptions)
            ?? throw new InvalidOperationException("StoreKit returned no purchase result.");
    }

    public async Task<StoreKitPurchase?> CurrentEntitlement(CancellationToken cancellationToken)
    {
        var resultJson = await Invoke(
            contextHandle => vhsk_current_entitlement(contextHandle, CompletedDelegate),
            cancellationToken).ConfigureAwait(false);
        return resultJson is "null" or ""
            ? null
            : JsonSerializer.Deserialize<StoreKitPurchase>(resultJson, JsonOptions);
    }

    // ------------------------------------------------------------ plumbing --

    private sealed class PendingCall
    {
        public TaskCompletionSource<string> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static Task<string> Invoke(Action<nint> nativeCall, CancellationToken cancellationToken)
    {
        var pending = new PendingCall();
        var handle = GCHandle.Alloc(pending);
        try {
            nativeCall(GCHandle.ToIntPtr(handle));
        }
        catch {
            handle.Free();
            throw;
        }
        return pending.Completion.Task.WaitAsync(cancellationToken);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CompletionDelegate(nint contextHandle, byte success, nint resultUtf8);

    // held in a static so the AOT'd trampoline outlives every in-flight call
    private static readonly CompletionDelegate CompletedDelegate = Completed;

    [MonoPInvokeCallback(typeof(CompletionDelegate))]
    private static void Completed(nint contextHandle, byte success, nint resultUtf8)
    {
        var handle = GCHandle.FromIntPtr(contextHandle);
        try {
            if (handle.Target is not PendingCall pending)
                return;
            var result = Marshal.PtrToStringUTF8(resultUtf8) ?? "";
            if (success != 0)
                pending.Completion.TrySetResult(result);
            else
                pending.Completion.TrySetException(new InvalidOperationException(
                    result == "" ? "StoreKit call failed." : result));
        }
        finally {
            handle.Free();
        }
    }

    // The Swift facade exports these via @_cdecl; they are statically linked in
    // through the xcframework NativeReference ("__Internal").
    [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
    private static extern void vhsk_load_products(string productIdsJson, nint context, CompletionDelegate callback);

    [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
    private static extern void vhsk_purchase(string productId, string appAccountToken, nint context,
        CompletionDelegate callback);

    [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl)]
    private static extern void vhsk_current_entitlement(nint context, CompletionDelegate callback);
}
