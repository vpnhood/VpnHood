# VpnHoodStoreKit — the StoreKit 2 Swift facade

Microsoft.iOS does not bind StoreKit 2's Swift-async API, so this tiny Swift
package exposes exactly the four calls the C# side needs over a **C ABI**
(`@_cdecl` functions + JSON strings + one completion callback):

| C function | StoreKit 2 |
| --- | --- |
| `vhsk_load_products` | `Product.products(for:)` + intro-offer eligibility |
| `vhsk_purchase` | `product.purchase(options: [.appAccountToken(uuid)])` |
| `vhsk_current_entitlement` | `Transaction.currentEntitlements` (newest) |
| `vhsk_show_manage_subscriptions` | `AppStore.showManageSubscriptions(in: scene)` |

The C# binding lives in `../StoreKitBridge/NativeStoreKitBridge.cs`; the two
files are a matched pair — change the contract in both or not at all.

## Building

On a Mac with Xcode 15+:

```bash
./build-xcframework.sh
```

This produces `swift/VpnHoodStoreKit.xcframework`, which the csproj references
conditionally (`Exists(...)`) — check the built xcframework in, so Windows/CI
builds never need a Swift toolchain. Until it exists, the C# project still
compiles; calling the billing provider then fails at runtime with a pointed
DllImport error, which is the intended "facade not built yet" signal.

## Design notes

- `transaction.finish()` is called immediately after a successful purchase:
  with the portal flow, delivery acknowledgment happens SERVER-side
  (POST /billing/purchases re-fetches the transaction from Apple), so an unfinished
  transaction would only cause repeated `updates` replays on the device.
- Purchases carry `appAccountToken` = the portal's external uid (a UUID), the
  same value Google receives as `obfuscatedAccountId` — the backend owns that
  mapping.
- Only auto-renewable subscriptions are mapped by `vhsk_load_products`;
  one-time products can be added to the same seam when Windows/consumables
  land.
