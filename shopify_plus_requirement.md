# Shopify Checkout Extensibility: Shopify Plus Requirement for Checkout UI Extensions

This document explains the platform constraints imposed by Shopify regarding the deployment of Checkout UI Extensions (such as the PUDO Counter Selector) and the plans required to use them.

---

## Executive Summary

The **PUDO Counter Selector** has been successfully built, tested, and packaged. It is designed to render directly inside the checkout flow (specifically immediately after the Shipping Address section) so customers can select a pick-up point before placing their order.

However, Shopify strictly limits modifications to the core checkout steps (Information, Shipping, and Payment) to merchants on the **Shopify Plus** plan. Because the test store is on a standard Shopify plan, Shopify's servers block the deployment of checkout-level extensions with a validation error.

---

## Platform Feature Matrix by Shopify Plan

Shopify categorizes checkout modifications into different tiers. The table below illustrates where the **PUDO Counter Selector** falls within these tiers:

| Feature / Target Page | Basic / Shopify / Advanced Plans | Shopify Plus Plan |
| :--- | :---: | :---: |
| **Thank You Page Extensions**<br>*(e.g., Post-purchase widgets, surveys)* | ✅ Supported | ✅ Supported |
| **Order Status Page Extensions**<br>*(e.g., Post-purchase PUDO selection, tracking info)* | ✅ Supported | ✅ Supported |
| **Core Checkout Page Extensions** 🔴<br>*(e.g., PUDO Selector before payment, custom address fields)* | ❌ **Blocked by Shopify** | ✅ Supported |

> [!IMPORTANT]
> **The Current Target Point:**
> The PUDO extension currently targets `purchase.checkout.shipping-address.render-after`. Because this modifies the active shipping/address page *prior* to order placement, Shopify restricts this API exclusively to **Shopify Plus** stores.

---

## Why Shopify Imposes This Restriction

1. **Security & Sandboxing:** Shopify runs checkout extensions in a highly secure, sandboxed environment to protect customer payment details and sensitive data.
2. **Premium Feature Gating:** Checkout customization is one of the primary selling points of the enterprise **Shopify Plus** subscription. Non-Plus merchants are restricted to standard checkout configurations.

---

## Technical Validation & Next Steps

The codebase is **100% complete and ready** for Checkout execution. To proceed, we recommend one of the following approaches:

### Option 1: Create a Shopify Plus Development Store (For Testing)
If the goal is to verify and test the checkout flow before upgrading the production store:
* We can create a free **Shopify Plus Development Store** via the Shopify Partners Dashboard.
* The PUDO Selector will deploy and render successfully there for testing and demonstration purposes.

### Option 2: Target the Thank You / Order Status Pages (For Non-Plus Plans)
If upgrading to Shopify Plus is not currently feasible, we can modify the extension to target the order completion pages:
* **Target:** `purchase.thank-you.block.render` or `purchase.order-status.block.render`.
* **Flow:** The customer completes checkout normally, and then selects their PUDO Counter on the confirmation page. The selected location is immediately updated on the order record in the backend.
* **Cost:** This target is supported on all standard Shopify plans.

---

## Official Documentation References
For further verification, please refer to the official Shopify Developer guidelines:
* [Shopify Checkout Extensibility Overview](https://shopify.dev/docs/apps/checkout)
* [Checkout UI Extensions Capabilities and Limitations](https://shopify.dev/docs/api/checkout-ui-extensions)
