import { reactExtension, useApi, useDeliveryGroups, useShippingAddress, useApplyAttributeChange, Link, Button, Text, BlockStack } from "@shopify/ui-extensions-react/checkout";
import { useState, useEffect } from "react";
 
export default reactExtension("purchase.checkout.block.render", () => <Extension />);
 
function Extension() {
  const { shop, applyShippingAddressChange } = useApi();
  const deliveryGroupsData = useDeliveryGroups();
  const shippingAddress = useShippingAddress();
  const applyAttributeChange = useApplyAttributeChange();
  const [selectedPudo, setSelectedPudo] = useState(null);
  const [widgetUrl, setWidgetUrl] = useState("");
  const [guid, setGuid] = useState("");
  const [isPolling, setIsPolling] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const deliveryGroup = deliveryGroupsData?.[0];
  const selectedDeliveryOption = deliveryGroup?.selectedDeliveryOption;
  const deliveryOptions = deliveryGroup?.deliveryOptions || [];
  const matchedOption = deliveryOptions.find(opt => opt.handle === selectedDeliveryOption?.handle);
  const selectedTitle = matchedOption?.title || "";
  const isCounterSelected = !!(selectedTitle.toLowerCase().includes("counter") || 
                              selectedDeliveryOption?.handle?.toLowerCase().includes("counter"));
  const baseUrl = "https://eboni-unprizable-discriminatingly.ngrok-free.dev";
  const myshopifyDomain = shop?.myshopifyDomain || "";
  
  const addressString = shippingAddress
    ? [
        shippingAddress.address1,
        shippingAddress.city,
        shippingAddress.province,
        shippingAddress.zip,
        shippingAddress.countryCode
      ].filter(Boolean).join(", ")
    : "South Africa";

  useEffect(() => {
    if (!isCounterSelected && selectedPudo) {
      console.log("Clearing PUDO attributes as counter shipping method is no longer selected.");
      setSelectedPudo(null);
      applyAttributeChange({ key: "pudo_code", type: "updateAttribute", value: "" });
      applyAttributeChange({ key: "pudo_name", type: "updateAttribute", value: "" });
      applyAttributeChange({ key: "pudo_addr1", type: "updateAttribute", value: "" });
      applyAttributeChange({ key: "pudo_city", type: "updateAttribute", value: "" });
      applyAttributeChange({ key: "pudo_zip", type: "updateAttribute", value: "" });
    }
  }, [isCounterSelected, selectedPudo]);

  useEffect(() => {
    if (!myshopifyDomain) return;

    const fetchWidgetUrl = async () => {
      try {
        setIsLoading(true);
        const url = `${baseUrl}/api/pudo/widget-url?shop=${myshopifyDomain}&address=${encodeURIComponent(addressString)}`;
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
  }, [myshopifyDomain, addressString]);

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
          console.log("Saving PUDO point attributes to cart notes:", pudo);

          await applyAttributeChange({
            key: "pudo_code",
            type: "updateAttribute",
            value: pudo.code
          });

          await applyAttributeChange({
            key: "pudo_name",
            type: "updateAttribute",
            value: pudo.name
          });

          await applyAttributeChange({
            key: "pudo_addr1",
            type: "updateAttribute",
            value: pudo.addr1
          });

          await applyAttributeChange({
            key: "pudo_city",
            type: "updateAttribute",
            value: pudo.city
          });

          await applyAttributeChange({
            key: "pudo_zip",
            type: "updateAttribute",
            value: pudo.pcode || pudo.zip
          });
        }
      } catch (error) {
        console.error("Polling error:", error);
      }
    }, 3000);
  };

  if (!isCounterSelected) {
    return null;
  }

  return (
    <BlockStack spacing="tight" border="dotted" padding="loose">
      <Text size="medium" weight="bold">
        📦 Collect from a PUDO Counter?
      </Text>
      
      {selectedPudo && (
        <BlockStack spacing="none">
          <Text size="small" appearance="success" weight="bold">
            ✓ Selected Pickup Point:
          </Text>
          <Text size="small" appearance="success">
            {selectedPudo.name}
          </Text>
          <Text size="small" appearance="subdued">
            {selectedPudo.addr1}{selectedPudo.addr2 ? `, ${selectedPudo.addr2}` : ""}
          </Text>
          <Text size="small" appearance="subdued">
            {selectedPudo.city}, {selectedPudo.pcode || selectedPudo.zip}
          </Text>
        </BlockStack>
      )}

      {isLoading ? (
        <Text size="small" appearance="subdued">
          Loading PUDO selector...
        </Text>
      ) : widgetUrl ? (
        isPolling ? (
          <Button disabled={true}>⏳ Waiting for selection...</Button>
        ) : (
          <Link to={widgetUrl} external={true} onPress={startPolling}>
            <Button>🔍 {selectedPudo ? "Change PUDO Counter" : "Select PUDO Counter"}</Button>
          </Link>
        )
      ) : (
        <Text size="small" appearance="critical">
          Unable to load PUDO selector. Please try refreshing.
        </Text>
      )}
    </BlockStack>
  );
}
