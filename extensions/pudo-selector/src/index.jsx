import { extend, Button, Text, BlockStack } from "@shopify/ui-extensions-react/checkout";
import { useState, useEffect } from "react";

export default extend("purchase.checkout.block.render", ({ extension }) => {
  const shop = extension.shop;
  const applyShippingAddressChange = extension.applyShippingAddressChange;
  const [selectedPudo, setSelectedPudo] = useState(null);
  const [widgetUrl, setWidgetUrl] = useState("");
  const [guid, setGuid] = useState("");
  const [isPolling, setIsPolling] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const baseUrl = "https://dev-skynet-online.azurewebsites.net";
  const myshopifyDomain = shop?.myshopifyDomain || "";

  useEffect(() => {
    if (!myshopifyDomain) return;

    const fetchWidgetUrl = async () => {
      try {
        setIsLoading(true);
        const url = `${baseUrl}/api/pudo/widget-url?shop=${myshopifyDomain}&address=South+Africa`;
        const response = await fetch(url, {
          headers: { "ngrok-skip-browser-warning": "69420" }
        });
        const data = await response.json();
        if (data?.success) {
          setWidgetUrl(data.widget_url);
          setGuid(data.guid);
        }
      } catch (error) {
        console.error("Failed to pre-fetch PUDO widget URL:", error);
      } finally {
        setIsLoading(false);
      }
    };

    fetchWidgetUrl();
  }, [myshopifyDomain]);

  const openWidget = () => {
    if (widgetUrl) {
      window.open(widgetUrl, "_blank");
      startPolling();
    }
  };

  const startPolling = () => {
    if (isPolling || !guid) return;
    setIsPolling(true);

    const interval = setInterval(async () => {
      try {
        const url = `${baseUrl}/api/pudo/selected/${guid}?shop=${myshopifyDomain}`;
        const response = await fetch(url, {
          headers: { "ngrok-skip-browser-warning": "69420" }
        });
        const data = await response.json();
        if (data?.success && data.pudo_point) {
          clearInterval(interval);
          setIsPolling(false);
          const pudo = data.pudo_point;
          setSelectedPudo(pudo);
          await applyShippingAddressChange({
            type: "updateShippingAddress",
            address: {
              firstName: "PUDO",
              lastName: pudo.name,
              address1: pudo.addr1,
              address2: pudo.addr2 || "",
              city: pudo.city,
              zip: pudo.pcode || pudo.zip,
              countryCode: "ZA"
            }
          });
        }
      } catch (error) {
        console.error("Polling error:", error);
      }
    }, 3000);
  };

  return (
    <BlockStack spacing="tight" border="dotted" padding="loose">
      <Text size="medium" weight="bold">
        📦 Collect from a PUDO Counter?
      </Text>
      {selectedPudo && (
        <Text size="small" appearance="success">
          ✓ Selected: {selectedPudo.name} — {selectedPudo.city}
        </Text>
      )}
      {isLoading ? (
        <Text size="small" appearance="subdued">
          Loading PUDO selector...
        </Text>
      ) : widgetUrl ? (
        <Button
          disabled={isPolling}
          onPress={openWidget}
        >
          {isPolling ? "⏳ Waiting for selection..." : "🔍 Select PUDO Counter"}
        </Button>
      ) : (
        <Text size="small" appearance="critical">
          Unable to load PUDO selector. Please try refreshing.
        </Text>
      )}
    </BlockStack>
  );
});
