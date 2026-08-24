// StoreKitBridge.swift — the C ABI facade over StoreKit 2 that
// VpnHood.AppLib.Ios.AppStore P/Invokes (NativeStoreKitBridge.cs).
//
// Contract (mirrored on the C# side, keep in sync):
//   - every function is @_cdecl, takes UTF-8 C strings, and completes exactly
//     once through the callback: (context, success, resultJsonUtf8)
//   - success=1 → resultJson is the payload; success=0 → resultJson is the
//     error message
//   - JSON field names are camelCase (System.Text.Json web defaults)
//
// Why this exists: Microsoft.iOS does not bind StoreKit 2's Swift-async API.
// This file is intentionally the ONLY Swift in the product — everything above
// it is C#.

import Foundation
import StoreKit
import UIKit

public typealias VhskCallback = @convention(c) (
    UnsafeMutableRawPointer?, UInt8, UnsafePointer<CChar>?
) -> Void

private func complete(_ context: UnsafeMutableRawPointer?, _ callback: VhskCallback, ok: Bool, _ json: String) {
    json.withCString { callback(context, ok ? 1 : 0, $0) }
}

private func completeError(_ context: UnsafeMutableRawPointer?, _ callback: VhskCallback, _ error: Error) {
    complete(context, callback, ok: false, String(describing: error))
}

// ---------------------------------------------------------------- products --

@_cdecl("vhsk_load_products")
public func vhsk_load_products(
    _ productIdsJson: UnsafePointer<CChar>,
    _ context: UnsafeMutableRawPointer?,
    _ callback: @escaping VhskCallback
) {
    let idsData = Data(bytes: productIdsJson, count: strlen(productIdsJson))
    Task {
        do {
            let ids = try JSONDecoder().decode([String].self, from: idsData)
            let products = try await Product.products(for: ids)

            let mapped: [[String: Any]] = try await withThrowingTaskGroup(of: [String: Any].self) { group in
                for product in products where product.subscription != nil {
                    group.addTask { try await mapProduct(product) }
                }
                var results: [[String: Any]] = []
                for try await item in group { results.append(item) }
                return results
            }

            let json = try JSONSerialization.data(withJSONObject: mapped)
            complete(context, callback, ok: true, String(data: json, encoding: .utf8) ?? "[]")
        } catch {
            completeError(context, callback, error)
        }
    }
}

private func mapProduct(_ product: Product) async throws -> [String: Any] {
    guard let subscription = product.subscription else {
        throw NSError(domain: "vhsk", code: 1,
                      userInfo: [NSLocalizedDescriptionKey: "\(product.id) is not a subscription"])
    }

    // the first price the user actually pays, and any free-trial phase, come from
    // the introductory offer — only when this user is still eligible for it
    var currentPrice = product.price
    var trialPeriodIso: String? = nil
    if let intro = subscription.introductoryOffer,
       await subscription.isEligibleForIntroOffer {
        if intro.paymentMode == .freeTrial {
            trialPeriodIso = isoDuration(intro.period)
        } else {
            currentPrice = intro.price
        }
    }

    var item: [String: Any] = [
        "id": product.id,
        "price": decimalToDouble(product.price),
        "currentPrice": decimalToDouble(currentPrice),
        "periodIso": isoDuration(subscription.subscriptionPeriod),
        "currencyCode": product.priceFormatStyle.currencyCode,
        "currencySymbol": product.priceFormatStyle.locale.currencySymbol ?? product.priceFormatStyle.currencyCode
    ]
    if let trialPeriodIso { item["trialPeriodIso"] = trialPeriodIso }
    return item
}

// NSDecimalNumber.doubleValue is lossy (46.99 becomes 46.989999999999995 — it multiplies the
// mantissa out by powers of ten), and that noise rides the JSON all the way into the price UI.
// Round-tripping through Decimal's canonical string yields the nearest double, which formats
// back to the exact store price.
private func decimalToDouble(_ value: Decimal) -> Double {
    Double("\(value)") ?? NSDecimalNumber(decimal: value).doubleValue
}

private func isoDuration(_ period: Product.SubscriptionPeriod) -> String {
    switch period.unit {
    case .day: return "P\(period.value)D"
    case .week: return "P\(period.value)W"
    case .month: return "P\(period.value)M"
    case .year: return "P\(period.value)Y"
    @unknown default: return "P\(period.value)D"
    }
}

// ---------------------------------------------------------------- purchase --

@_cdecl("vhsk_purchase")
public func vhsk_purchase(
    _ productIdC: UnsafePointer<CChar>,
    _ appAccountTokenC: UnsafePointer<CChar>,
    _ context: UnsafeMutableRawPointer?,
    _ callback: @escaping VhskCallback
) {
    let productId = String(cString: productIdC)
    let appAccountToken = String(cString: appAccountTokenC)
    Task {
        do {
            guard let uuid = UUID(uuidString: appAccountToken) else {
                throw NSError(domain: "vhsk", code: 2,
                              userInfo: [NSLocalizedDescriptionKey: "appAccountToken is not a UUID"])
            }
            guard let product = try await Product.products(for: [productId]).first else {
                throw NSError(domain: "vhsk", code: 3,
                              userInfo: [NSLocalizedDescriptionKey: "unknown product: \(productId)"])
            }

            let result = try await product.purchase(options: [.appAccountToken(uuid)])
            switch result {
            case .success(let verification):
                // StoreKit already verified the JWS on-device; the SIGNED payload is
                // what travels to the portal, which re-verifies server-side anyway.
                let transaction = try checked(verification)
                let payload: [String: Any] = [
                    "state": "purchased",
                    "transactionId": String(transaction.id),
                    "originalTransactionId": String(transaction.originalID),
                    "jws": verification.jwsRepresentation
                ]
                await transaction.finish()
                let json = try JSONSerialization.data(withJSONObject: payload)
                complete(context, callback, ok: true, String(data: json, encoding: .utf8) ?? "{}")
            case .pending:
                complete(context, callback, ok: true, #"{"state":"pending"}"#)
            case .userCancelled:
                complete(context, callback, ok: true, #"{"state":"cancelled"}"#)
            @unknown default:
                complete(context, callback, ok: true, #"{"state":"pending"}"#)
            }
        } catch {
            completeError(context, callback, error)
        }
    }
}

// ------------------------------------------------------------ entitlements --

@_cdecl("vhsk_current_entitlement")
public func vhsk_current_entitlement(
    _ context: UnsafeMutableRawPointer?,
    _ callback: @escaping VhskCallback
) {
    Task {
        do {
            var newest: (transaction: Transaction, jws: String)? = nil
            for await verification in Transaction.currentEntitlements {
                let transaction = try checked(verification)
                if newest == nil || transaction.purchaseDate > newest!.transaction.purchaseDate {
                    newest = (transaction, verification.jwsRepresentation)
                }
            }
            guard let newest else {
                complete(context, callback, ok: true, "null")
                return
            }
            let payload: [String: Any] = [
                "state": "purchased",
                "transactionId": String(newest.transaction.id),
                "originalTransactionId": String(newest.transaction.originalID),
                "jws": newest.jws
            ]
            let json = try JSONSerialization.data(withJSONObject: payload)
            complete(context, callback, ok: true, String(data: json, encoding: .utf8) ?? "null")
        } catch {
            completeError(context, callback, error)
        }
    }
}

// ------------------------------------------------------- manage subscriptions --

// Apple's own manage-subscriptions sheet, presented INSIDE the app: no browser, no
// switch to the App Store. Returns once the sheet is dismissed. In the sandbox the
// sheet is empty (StoreKit testing has no real subscriptions) — that is an
// environment limit, not a failure, so it still completes successfully.
@_cdecl("vhsk_show_manage_subscriptions")
public func vhsk_show_manage_subscriptions(
    _ context: UnsafeMutableRawPointer?,
    _ callback: @escaping VhskCallback
) {
    Task { @MainActor in
        do {
            let scenes = UIApplication.shared.connectedScenes.compactMap { $0 as? UIWindowScene }
            guard let scene = scenes.first(where: { $0.activationState == .foregroundActive })
                    ?? scenes.first else {
                throw NSError(domain: "vhsk", code: 4,
                              userInfo: [NSLocalizedDescriptionKey: "no window scene to present in"])
            }
            try await AppStore.showManageSubscriptions(in: scene)
            complete(context, callback, ok: true, "null")
        } catch {
            completeError(context, callback, error)
        }
    }
}

private func checked<T>(_ result: VerificationResult<T>) throws -> T {
    switch result {
    case .verified(let value): return value
    case .unverified(_, let error): throw error
    }
}
